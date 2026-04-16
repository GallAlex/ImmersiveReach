#pragma once

typedef struct _RaycastingState
{
    int width = 0;
    int height = 0;
    sutil::Scene *scene;
    int rays_per_pixel = 1;

    float radius = 0.0f;
    float3* ray_start_surface_points;
    CUdeviceptr d_ray_start_surface_points;

    float3* instrument_volume_points;
    CUdeviceptr d_instrument_volume_points;

    OptixDeviceContext context = 0;

    OptixPipelineCompileOptions pipeline_compile_options = {};
    OptixModule ptx_module = 0;
    OptixPipeline pipeline_1 = 0;
    OptixPipeline pipeline_2 = 0;

    OptixProgramGroup raygen_prog_group = 0;
    OptixProgramGroup miss_prog_group = 0;
    OptixProgramGroup hit_prog_group = 0;

    Params params = {};
    Params* d_params = 0;
    OptixShaderBindingTable sbt = {};

    sutil::Texture mask = {};

    CUstream stream = 0;
} RaycastingState;