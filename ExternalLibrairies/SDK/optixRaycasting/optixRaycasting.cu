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

#include <optix.h>

#include "optixRaycasting.h"
#include "optixRaycastingKernels.h"

#include "cuda/LocalGeometry.h"
#include "cuda/whitted.h"
#include "cuda/LocalShading.h"

#include <sutil/vec_math.h>

extern "C" {
__constant__ Params params;
}

extern "C" __global__ void __raygen__from_buffer()
{
    const uint3        idx        = optixGetLaunchIndex();
    const uint3        dim        = optixGetLaunchDimensions();
    const unsigned int linear_idx = idx.z * dim.y * dim.x + idx.y * dim.x + idx.x;

    unsigned int t, nx, ny, nz; // distance, normal
    unsigned int bx, by; // barycentric coordinates
    unsigned int triangle_idx; // triangle index
    unsigned int tcx, tcy; // texture coordinates
    unsigned int instance_id;
    unsigned int hitx, hity, hitz; // hit point
    Ray          ray = params.rays[linear_idx];
    optixTrace( params.handle, ray.origin, ray.dir, ray.tmin, ray.tmax, 0.0f, OptixVisibilityMask( 1 ),
                OPTIX_RAY_FLAG_NONE, RAY_TYPE_RADIANCE, RAY_TYPE_COUNT, RAY_TYPE_RADIANCE, t, nx, ny, nz, bx, by, triangle_idx, tcx, tcy, instance_id, hitx, hity, hitz );

    Hit hit;
    hit.t                   = __uint_as_float( t );
    hit.geom_normal.x       = __uint_as_float( nx );
    hit.geom_normal.y       = __uint_as_float( ny );
    hit.geom_normal.z       = __uint_as_float( nz );
    hit.barycentric_coords.x = __uint_as_float( bx );
    hit.barycentric_coords.y = __uint_as_float( by );
    hit.triangle_idx       = triangle_idx;
    hit.texcoord.x         = __uint_as_float( tcx );
    hit.texcoord.y         = __uint_as_float( tcy );
    hit.instance_id        = instance_id;
    hit.hit_point.x        = __uint_as_float( hitx );
    hit.hit_point.y        = __uint_as_float( hity );
    hit.hit_point.z        = __uint_as_float( hitz );
    params.hits[linear_idx] = hit;
}

extern "C" __global__ void __miss__buffer_miss()
{
    optixSetPayload_0( __float_as_uint( -1.0f ) );
    optixSetPayload_1( __float_as_uint( 1.0f ) );
    optixSetPayload_2( __float_as_uint( 0.0f ) );
    optixSetPayload_3( __float_as_uint( 0.0f ) );
    optixSetPayload_4( __float_as_uint( 0.0f ) );
    optixSetPayload_5( __float_as_uint( 0.0f ) );
    optixSetPayload_6( 0 );
    optixSetPayload_7( __float_as_uint( 0.0f ) );
    optixSetPayload_8( __float_as_uint( 0.0f ) );
    optixSetPayload_9( 0 );
    optixSetPayload_10( __float_as_uint( 0.0f ) );
    optixSetPayload_11( __float_as_uint( 0.0f ) );
    optixSetPayload_12( __float_as_uint( 0.0f ) );
}

extern "C" __global__ void __closesthit__buffer_hit()
{
    const unsigned int t = optixGetRayTmax();

    whitted::HitGroupData* rt_data = (whitted::HitGroupData*)optixGetSbtDataPointer();
    LocalGeometry          geom    = getLocalGeometry( rt_data->geometry_data );

    const float2 barycentrics = optixGetTriangleBarycentrics();
    const int   prim_idx     = optixGetPrimitiveIndex();
    const int instance_id     = optixGetInstanceId();
    const float3 hit_point    = optixGetWorldRayOrigin() + t * optixGetWorldRayDirection();

    // Set the hit data
    optixSetPayload_0( __float_as_uint( t ) );
    optixSetPayload_1( __float_as_uint( geom.N.x ) );
    optixSetPayload_2( __float_as_uint( geom.N.y ) );
    optixSetPayload_3( __float_as_uint( geom.N.z ) );
    optixSetPayload_4( __float_as_uint( barycentrics.x ) );
    optixSetPayload_5( __float_as_uint( barycentrics.y ) );
    optixSetPayload_6( prim_idx );
    optixSetPayload_7( __float_as_uint( geom.texcoord[0].UV.x ) );
    optixSetPayload_8( __float_as_uint( geom.texcoord[0].UV.y ) );
    optixSetPayload_9( instance_id );
    optixSetPayload_10( __float_as_uint( hit_point.x ) );
    optixSetPayload_11( __float_as_uint( hit_point.y ) );
    optixSetPayload_12( __float_as_uint( hit_point.z ) );
}

extern "C" __global__ void __anyhit__texture_mask()
{
    whitted::HitGroupData* rt_data = (whitted::HitGroupData*)optixGetSbtDataPointer();

    if( rt_data->material_data.alpha_mode == MaterialData::ALPHA_MODE_MASK )
    {
        LocalGeometry geom = getLocalGeometry( rt_data->geometry_data );
        float4        mask = sampleTexture<float4>( rt_data->material_data.pbr.base_color_tex, geom );
        if( mask.w < rt_data->material_data.alpha_cutoff )
        {
            optixIgnoreIntersection();
        }
    }
}