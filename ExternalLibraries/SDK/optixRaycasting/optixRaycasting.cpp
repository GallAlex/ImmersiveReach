//
// Copyright (c) 2023, NVIDIA CORPORATION. All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions
// are met:
//  * Redistributions of source code must retain the above copyright
//    notice, this list of conditions and the following disclaimer.
//  * Redistributions in binary form must reproduce the above copyright
//    notice, this list of conditions and the following disclaimer in the
//    documentation and/or other materials provided with the distribution.
//  * Neither the name of NVIDIA CORPORATION nor the names of its
//    contributors may be used to endorse or promote products derived
//    from this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ``AS IS'' AND ANY
// EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
// PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT OWNER OR
// CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
// EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
// PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
// PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY
// OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//


#include <cuda_runtime.h>

#include <optix.h>
#include <optix_function_table_definition.h>
#include <optix_stack_size.h>
#include <optix_stubs.h>

#include <sampleConfig.h>

#include "cuda/whitted.h"
#include <sutil/CUDAOutputBuffer.h>
#include <sutil/Matrix.h>
#include <sutil/Record.h>
#include <sutil/Scene.h>
#include <sutil/sutil.h>

#include "optixRaycasting.h"
#include "optixRaycastingKernels.h"
#include "RaycastingState.h"
#include "DebugCPP.h"
#include "RaycastingOutput.h"

#include <iomanip>

#if defined(_WIN32) || defined(__WIN32__) || defined(__WINDOWS__)
#define DLLExport __declspec(dllexport)
#else
#define DLLExport
#endif

typedef sutil::Record<whitted::HitGroupData> HitGroupRecord;

void printUsageAndExit( const char* argv0 )
{
    std::cerr << "Usage  : " << argv0 << " [options]\n"
              << "Options:\n"
              << "  -h | --help                           Print this usage message\n"
              << "  -f | --file  <prefix>                 Prefix of output file\n"
              << "  -m | --model <model.gltf>             Model to be rendered\n"
              << "  -w | --width <number>                 Output image width\n"
              << std::endl;
            
    Debug::LogError(std::string("Usage: ") + std::string(argv0) + std::string(" [options]\n")
        + std::string("Options:\n")
        + std::string("  -h | --help                           Print this usage message\n")
        + std::string("  -f | --file  <prefix>                 Prefix of output file\n")
        + std::string("  -m | --model <model.gltf>             Model to be rendered\n")
        + std::string("  -w | --width <number>                 Output image width\n"));
    exit( 1 );
}


void createModule( RaycastingState& state )
{
    OptixModuleCompileOptions module_compile_options = {};
#if !defined( NDEBUG )
    module_compile_options.optLevel   = OPTIX_COMPILE_OPTIMIZATION_LEVEL_0;
    module_compile_options.debugLevel = OPTIX_COMPILE_DEBUG_LEVEL_FULL;
#else
    module_compile_options.optLevel   = OPTIX_COMPILE_OPTIMIZATION_DEFAULT;
    module_compile_options.debugLevel = OPTIX_COMPILE_DEBUG_LEVEL_MINIMAL;
#endif

    state.pipeline_compile_options.usesMotionBlur        = false;
    state.pipeline_compile_options.traversableGraphFlags = OPTIX_TRAVERSABLE_GRAPH_FLAG_ALLOW_SINGLE_LEVEL_INSTANCING;
    state.pipeline_compile_options.numPayloadValues      = 13;
    state.pipeline_compile_options.numAttributeValues    = 2;
    state.pipeline_compile_options.exceptionFlags        = OPTIX_EXCEPTION_FLAG_NONE;
    state.pipeline_compile_options.pipelineLaunchParamsVariableName = "params";

    size_t      inputSize = 0;
    const char* input     = sutil::getInputData( OPTIX_SAMPLE_NAME, OPTIX_SAMPLE_DIR, "optixRaycasting.cu", inputSize );
    OPTIX_CHECK_LOG( optixModuleCreate( state.context, &module_compile_options, &state.pipeline_compile_options, input,
                                        inputSize, LOG, &LOG_SIZE, &state.ptx_module ) );
}

void createProgramGroups( RaycastingState& state )
{
    OptixProgramGroupOptions program_group_options = {};

    OptixProgramGroupDesc raygen_prog_group_desc    = {};
    raygen_prog_group_desc.kind                     = OPTIX_PROGRAM_GROUP_KIND_RAYGEN;
    raygen_prog_group_desc.raygen.module            = state.ptx_module;
    raygen_prog_group_desc.raygen.entryFunctionName = "__raygen__from_buffer";

    OPTIX_CHECK_LOG( optixProgramGroupCreate( state.context, &raygen_prog_group_desc,
                                              1,  // num program groups
                                              &program_group_options, LOG, &LOG_SIZE, &state.raygen_prog_group ) );

    OptixProgramGroupDesc miss_prog_group_desc  = {};
    miss_prog_group_desc.kind                   = OPTIX_PROGRAM_GROUP_KIND_MISS;
    miss_prog_group_desc.miss.module            = state.ptx_module;
    miss_prog_group_desc.miss.entryFunctionName = "__miss__buffer_miss";
    OPTIX_CHECK_LOG( optixProgramGroupCreate( state.context, &miss_prog_group_desc,
                                              1,  // num program groups
                                              &program_group_options, LOG, &LOG_SIZE, &state.miss_prog_group ) );


    OptixProgramGroupDesc hit_prog_group_desc = {};
    hit_prog_group_desc.kind                  = OPTIX_PROGRAM_GROUP_KIND_HITGROUP;
    hit_prog_group_desc.hitgroup.moduleAH            = state.ptx_module;
    hit_prog_group_desc.hitgroup.entryFunctionNameAH = "__anyhit__texture_mask";
    hit_prog_group_desc.hitgroup.moduleCH            = state.ptx_module;
    hit_prog_group_desc.hitgroup.entryFunctionNameCH = "__closesthit__buffer_hit";
    OPTIX_CHECK_LOG( optixProgramGroupCreate( state.context, &hit_prog_group_desc,
                                              1,  // num program groups
                                              &program_group_options, LOG, &LOG_SIZE, &state.hit_prog_group ) );
}


void createPipelines( RaycastingState& state )
{
    const uint32_t    max_trace_depth   = 1;
    OptixProgramGroup program_groups[3] = {state.raygen_prog_group, state.miss_prog_group, state.hit_prog_group};

    OptixPipelineLinkOptions pipeline_link_options = {};
    pipeline_link_options.maxTraceDepth            = max_trace_depth;

    OPTIX_CHECK_LOG( optixPipelineCreate( state.context, &state.pipeline_compile_options, &pipeline_link_options,
                                          program_groups, sizeof( program_groups ) / sizeof( program_groups[0] ), LOG,
                                          &LOG_SIZE, &state.pipeline_1 ) );
    OPTIX_CHECK_LOG( optixPipelineCreate( state.context, &state.pipeline_compile_options, &pipeline_link_options,
                                          program_groups, sizeof( program_groups ) / sizeof( program_groups[0] ), LOG,
                                          &LOG_SIZE, &state.pipeline_2 ) );

    OptixStackSizes stack_sizes_1 = {};
    OptixStackSizes stack_sizes_2 = {};
    for( auto& prog_group : program_groups )
    {
        OPTIX_CHECK( optixUtilAccumulateStackSizes( prog_group, &stack_sizes_1, state.pipeline_1 ) );
        OPTIX_CHECK( optixUtilAccumulateStackSizes( prog_group, &stack_sizes_2, state.pipeline_2 ) );
    }

    uint32_t direct_callable_stack_size_from_traversal;
    uint32_t direct_callable_stack_size_from_state;
    uint32_t continuation_stack_size;
    OPTIX_CHECK( optixUtilComputeStackSizes( &stack_sizes_1, max_trace_depth,
                                             0,  // maxCCDepth
                                             0,  // maxDCDEpth
                                             &direct_callable_stack_size_from_traversal,
                                             &direct_callable_stack_size_from_state, &continuation_stack_size ) );
    OPTIX_CHECK( optixPipelineSetStackSize( state.pipeline_1, direct_callable_stack_size_from_traversal,
                                            direct_callable_stack_size_from_state, continuation_stack_size,
                                            2  // maxTraversableDepth
                                            ) );
    OPTIX_CHECK( optixUtilComputeStackSizes( &stack_sizes_2, max_trace_depth,
                                             0,  // maxCCDepth
                                             0,  // maxDCDEpth
                                             &direct_callable_stack_size_from_traversal,
                                             &direct_callable_stack_size_from_state, &continuation_stack_size ) );
    OPTIX_CHECK( optixPipelineSetStackSize( state.pipeline_2, direct_callable_stack_size_from_traversal,
                                            direct_callable_stack_size_from_state, continuation_stack_size,
                                            2  // maxTraversableDepth
                                            ) );
}


void createSBT( RaycastingState& state )
{
    // raygen
    CUdeviceptr  d_raygen_record    = 0;
    const size_t raygen_record_size = sizeof( sutil::EmptyRecord );
    CUDA_CHECK( cudaMalloc( reinterpret_cast<void**>( &d_raygen_record ), raygen_record_size ) );

    sutil::EmptyRecord rg_record;
    OPTIX_CHECK( optixSbtRecordPackHeader( state.raygen_prog_group, &rg_record ) );
    CUDA_CHECK( cudaMemcpy( reinterpret_cast<void*>( d_raygen_record ), &rg_record, raygen_record_size, cudaMemcpyHostToDevice ) );

    CUdeviceptr  d_instrument_volume_points    = 0;
    const size_t instrument_volume_points_size = sizeof( float3 ) * state.rays_per_pixel;
    CUDA_CHECK( cudaMalloc( reinterpret_cast<void**>( &d_instrument_volume_points ), instrument_volume_points_size ) );
    CUDA_CHECK( cudaMemcpy( reinterpret_cast<void*>( d_instrument_volume_points ), state.instrument_volume_points, instrument_volume_points_size, cudaMemcpyHostToDevice ) );
    state.d_instrument_volume_points = d_instrument_volume_points;

    // miss
    CUdeviceptr  d_miss_record    = 0;
    const size_t miss_record_size = sizeof( sutil::EmptyRecord );
    CUDA_CHECK( cudaMalloc( reinterpret_cast<void**>( &d_miss_record ), miss_record_size ) );

    sutil::EmptyRecord ms_record;
    OPTIX_CHECK( optixSbtRecordPackHeader( state.miss_prog_group, &ms_record ) );
    CUDA_CHECK( cudaMemcpy( reinterpret_cast<void*>( d_miss_record ), &ms_record, miss_record_size, cudaMemcpyHostToDevice ) );

    // hit group
    std::vector<HitGroupRecord> hitgroup_records;
    for( const auto& mesh : state.scene->meshes() )
    {
        for( size_t i = 0; i < mesh->material_idx.size(); ++i )
        {
            HitGroupRecord rec = {};
            OPTIX_CHECK( optixSbtRecordPackHeader( state.hit_prog_group, &rec ) );
            GeometryData::TriangleMesh triangle_mesh = {};
            triangle_mesh.positions                  = mesh->positions[i];
            triangle_mesh.normals                    = mesh->normals[i];
            for( size_t j = 0; j < GeometryData::num_texcoords; ++j )
                triangle_mesh.texcoords[j] = mesh->texcoords[j][i];
            triangle_mesh.indices = mesh->indices[i];
            rec.data.geometry_data.setTriangleMesh( triangle_mesh );
            rec.data.material_data = state.scene->materials()[mesh->material_idx[i]];
            hitgroup_records.push_back( rec );
        }
    }

    CUdeviceptr  d_hitgroup_record    = 0;
    const size_t hitgroup_record_size = sizeof( HitGroupRecord );
    CUDA_CHECK( cudaMalloc( reinterpret_cast<void**>( &d_hitgroup_record ), hitgroup_record_size * hitgroup_records.size() ) );
    CUDA_CHECK( cudaMemcpy( reinterpret_cast<void*>( d_hitgroup_record ), hitgroup_records.data(),
                            hitgroup_record_size * hitgroup_records.size(), cudaMemcpyHostToDevice ) );

    state.sbt.raygenRecord                = d_raygen_record;
    state.sbt.missRecordBase              = d_miss_record;
    state.sbt.missRecordStrideInBytes     = static_cast<uint32_t>( miss_record_size );
    state.sbt.missRecordCount             = RAY_TYPE_COUNT;
    state.sbt.hitgroupRecordBase          = d_hitgroup_record;
    state.sbt.hitgroupRecordStrideInBytes = static_cast<uint32_t>( hitgroup_record_size );
    state.sbt.hitgroupRecordCount         = static_cast<int>( hitgroup_records.size() );

    Ray*   rays_d             = 0;
    size_t rays_size_in_bytes = sizeof( Ray ) * state.width * state.height * state.rays_per_pixel;
    CUDA_CHECK( cudaMalloc( &rays_d, rays_size_in_bytes ) );

    Hit*   hits_d             = 0;
    size_t hits_size_in_bytes = sizeof( Hit ) * state.width * state.height * state.rays_per_pixel;
    CUDA_CHECK( cudaMalloc( &hits_d, hits_size_in_bytes ) );

    state.params            = {state.scene->traversableHandle(), rays_d, hits_d};

    CUDA_CHECK( cudaStreamCreate( &state.stream ) );
    CUDA_CHECK( cudaMalloc( reinterpret_cast<void**>( &state.d_params ), sizeof( Params ) ) );
}


void bufferRays( RaycastingState& state, float3 ray_origin, Quaternion rotation, float3 planePosition, Quaternion planeRotation, float3 planeScale )
{
    createRaysOrthoOnDevice( state.params.rays, state.width, state.height, ray_origin, state.rays_per_pixel, reinterpret_cast<float3*>(state.d_instrument_volume_points), rotation, planePosition, planeRotation, planeScale );
    CUDA_CHECK( cudaGetLastError() );
}


void launch( RaycastingState& state )
{
    CUDA_CHECK( cudaMemcpyAsync( reinterpret_cast<void*>( state.d_params ), &state.params, sizeof( Params ),
                                 cudaMemcpyHostToDevice, state.stream ) );

    OPTIX_CHECK( optixLaunch( state.pipeline_1, state.stream, reinterpret_cast<CUdeviceptr>( state.d_params ), sizeof( Params ),
                              &state.sbt, state.width * state.rays_per_pixel, state.height, 1 ) );

    CUDA_SYNC_CHECK();
}


RaycastingOutput* shadeHits( RaycastingState& state )
{
    sutil::CUDAOutputBufferType     output_buffer_type = sutil::CUDAOutputBufferType::CUDA_DEVICE;
    sutil::CUDAOutputBuffer<char> output_buffer( output_buffer_type, state.width, state.height );
    // sutil::CUDAOutputBuffer<HitReturn> hit_buffer( output_buffer_type, state.width, state.height * state.rays_per_pixel );

    int array_size = state.width * state.height;

    // shadeHitsOnDevice( output_buffer.map(), array_size, state.rays_per_pixel, state.params.hits, hit_buffer.map() );
    shadeHitsOnDevice( output_buffer.map(), array_size, state.rays_per_pixel, state.params.hits );
    CUDA_CHECK( cudaGetLastError() );
    
    output_buffer.unmap();
    char * buffer         = output_buffer.getHostPointer();
    char * data = new char[array_size];
    memcpy(data, buffer, array_size * sizeof(char));

    // copy hits
    Hit * hits = new Hit[array_size * state.rays_per_pixel];
    CUDA_CHECK( cudaMemcpy( hits, state.params.hits, array_size * state.rays_per_pixel * sizeof(Hit), cudaMemcpyDeviceToHost ) );


    // hit_buffer.unmap();
    // HitReturn* hits = hit_buffer.getHostPointer();
    // HitReturn* hits_data = new HitReturn[array_size * state.rays_per_pixel];
    // memcpy(hits_data, hits, array_size * state.rays_per_pixel * sizeof(HitReturn));
    // output->hits = hits_data;

    RaycastingOutput* output = new RaycastingOutput();
    output->arraySize = array_size;
    output->width = state.width;
    output->height = state.height;
    output->array = data;
    output->hits = hits;
    
    return output;
}

extern "C" DLLExport void earlyCleanup( RaycastingState* state, RaycastingOutput* output )
{
    delete[] output->array;
    delete[] output->hits;
    delete output;
}

void cleanup( RaycastingState& state )
{
    CUDA_CHECK( cudaFree( reinterpret_cast<void*>( state.params.rays ) ) );
    CUDA_CHECK( cudaFree( reinterpret_cast<void*>( state.params.hits ) ) );

    CUDA_CHECK( cudaFree( reinterpret_cast<void*>( state.d_params ) ) );
    CUDA_CHECK( cudaStreamDestroy( state.stream ) );

    OPTIX_CHECK( optixPipelineDestroy( state.pipeline_1 ) );
    OPTIX_CHECK( optixPipelineDestroy( state.pipeline_2 ) );
    OPTIX_CHECK( optixProgramGroupDestroy( state.raygen_prog_group ) );
    OPTIX_CHECK( optixProgramGroupDestroy( state.miss_prog_group ) );
    OPTIX_CHECK( optixProgramGroupDestroy( state.hit_prog_group ) );
    OPTIX_CHECK( optixModuleDestroy( state.ptx_module ) );

    CUDA_CHECK( cudaFree( reinterpret_cast<void*>( state.sbt.raygenRecord ) ) );
    CUDA_CHECK( cudaFree( reinterpret_cast<void*>( state.sbt.missRecordBase ) ) );
    CUDA_CHECK( cudaFree( reinterpret_cast<void*>( state.sbt.hitgroupRecordBase ) ) );

    CUDA_CHECK( cudaDestroyTextureObject( state.mask.texture ) );
    CUDA_CHECK( cudaFreeArray( state.mask.array ) );
    CUDA_CHECK( cudaFree( reinterpret_cast<void*>( state.d_instrument_volume_points ) ) );
    
}

// UNUSED
void computeRayStartSurfacePoints( RaycastingState& state )
{
    float3* ray_start_surface_points = new float3[state.rays_per_pixel];
    float phi = (1.0f + sqrt(5.0f)) / 2.0f; // golden ratio
    float angle_stride = 360.0f * phi;
    int b = (int)sqrt((float)state.rays_per_pixel);  // # number of boundary points
    float divisor = sqrt((float)state.rays_per_pixel - (b + 1.0f) / 2.0f);
    ray_start_surface_points[0] = make_float3(0.0f, 0.0f, 0.0f);
    for( int i = 1; i < state.rays_per_pixel; ++i )
    {
        float r = i > state.rays_per_pixel - b ? state.radius : state.radius * sqrt((float)i - 0.5f) / divisor;
        float theta = (float)i * angle_stride;
        float sampleX = r * cosf(theta);
        float sampleY = r * sinf(theta);
        ray_start_surface_points[i] = make_float3(sampleX, sampleY, 0.0f);
    }
    state.ray_start_surface_points = ray_start_surface_points;
}

extern "C" DLLExport RaycastingState* initializeOptix(char* cs_infile, int displayWidth, int displayHeight, int raysPerPixel, float3* instrumentVolumeSample)
{

    std::string infile = cs_infile;
    RaycastingState* state = new RaycastingState();
    state->width = displayWidth;
    state->height = displayHeight;
    state->rays_per_pixel = raysPerPixel;

    float3* instrument_volume_points = new float3[raysPerPixel];
    for (int i = 0; i < raysPerPixel; ++i)
    {
        instrument_volume_points[i] = make_float3(instrumentVolumeSample[i].x, instrumentVolumeSample[i].y, instrumentVolumeSample[i].z);
    }
    state->instrument_volume_points = instrument_volume_points;

    try{
        state->scene = new sutil::Scene();
        sutil::loadScene( infile.c_str(), *(state->scene) );
        state->scene->createContext();

        state->scene->buildMeshAccels();
        state->scene->buildInstanceAccel( RAY_TYPE_COUNT );
        state->context = state->scene->context();

        OPTIX_CHECK( optixInit() );  // Need to initialize function table
        createModule( *state );
        createProgramGroups( *state );
        createPipelines( *state );
        createSBT( *state );

    }
    catch( std::exception& e )
    {
        std::cerr << "Caught exception: " << e.what() << std::endl;
        Debug::LogError(std::string("initializeOptix : ") + std::string(e.what()));
    }
    return state;
}

extern "C" DLLExport void cleanupOptix(RaycastingState* state)
{
    try{
        cleanup( *state );
        delete state->scene;
        delete [] state->instrument_volume_points;
        delete state;
    }
    catch( std::exception& e )
    {
        std::cerr << "Caught exception: " << e.what() << std::endl;   
        Debug::LogError(std::string("cleanupOptix : ") + std::string(e.what()));
    }
}

extern "C" DLLExport RaycastingOutput* performRaycasting(RaycastingState* state, float3* rayStartPosition, Quaternion* rayStartRotation, float3* planePosition, Quaternion* planeRotation, float3* planeScale)
{
    try{
        printf("Performing raycasting\n");
        bufferRays( *state, *rayStartPosition, *rayStartRotation, *planePosition, *planeRotation, *planeScale );
        launch( *state );
        RaycastingOutput* output_data = shadeHits( *state );
        return output_data;
    }
    catch( std::exception& e )
    {
        std::cerr << "Caught exception: " << e.what() << std::endl;
        Debug::LogError(std::string("performRaycasting : ") + std::string(e.what()));
    }
}


int main( int argc, char** argv )
{
    std::string     infile, outfile;
    RaycastingState* state;

    try
    {
        float3* instrumentVolumeSample = new float3[3];
        instrumentVolumeSample[0] = make_float3(0.0f, 0.0f, 0.0f);
        instrumentVolumeSample[1] = make_float3(1.0f, 0.0f, 0.0f);
        instrumentVolumeSample[2] = make_float3(0.0f, 1.0f, 0.0f);
        state = initializeOptix("C:\\Users\\mauda\\maud\\Uni Passau\\Masterthesis\\Unity\\CrepuscularRays\\ExportedScene.gltf", 4, 4, 3, instrumentVolumeSample);
        std::cout << "Optix initialized" << std::endl;
        performRaycasting(state, new float3{0.0f, 1.0f, -8.0f}, new Quaternion{0.0f, 0.0f, 0.0f, 1.0f}, new float3{-2.0f, 2.0f, 0.0f}, new Quaternion{0.0f, 0.0f, 0.0f, 1.0f}, new float3{1.0f, 1.0f, 1.0f});
        std::cout << "Raycasting performed" << std::endl;
        cleanupOptix(state);
        std::cout << "Optix cleaned up" << std::endl;
    }
    catch( std::exception& e )
    {
        std::cerr << "Caught exception: " << e.what() << std::endl;
        return 1;
    }

    return 0;
}
