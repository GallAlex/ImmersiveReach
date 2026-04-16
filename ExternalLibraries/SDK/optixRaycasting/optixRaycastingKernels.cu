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

#include "optixRaycastingKernels.h"

#include <sutil/vec_math.h>
#include <math_constants.h>
#include <stdio.h>


inline int idivCeil( int x, int y )
{
    return ( x + y - 1 ) / y;
}

__device__ float3 operator*( const Quaternion& q, const float3& v )
{
    float3 qv = make_float3( q.x, q.y, q.z );
    float3 t = cross( qv, v ) * 2.0f;
    return v + t * q.w + cross( qv, t );
}

__global__ void createRaysOrthoKernel( Ray* rays, int width, int height, float3 ray_origin, int rays_per_pixel, float3* instrument_volume_points, Quaternion rotation, float3 planePosition, Quaternion planeRotation, float3 planeScale )
{
    const float3 converted_ray_origin = make_float3( -ray_origin.x, ray_origin.y, ray_origin.z );
    const int width_ray_x = threadIdx.x + blockIdx.x * blockDim.x;
    
    const int k = width_ray_x % rays_per_pixel;
    const int rayx = width_ray_x / rays_per_pixel;
    const int rayy = threadIdx.y + blockIdx.y * blockDim.y;
    if( rayx >= width || rayy >= height )
        return;

    const int idx    = width_ray_x + rayy * width * rays_per_pixel;

    float3 rotatedPoint = rotation * instrument_volume_points[k];
    rays[idx].origin = make_float3(-rotatedPoint.x, rotatedPoint.y, rotatedPoint.z) + converted_ray_origin;
    rays[idx].tmin   = 0.0f;

    float x = (rayx / (float)width) * 10 - 10 / 2;
    float y = (rayy / (float)height) * 10 - 10 / 2;
    float3 pixelPosition = planePosition + planeRotation * make_float3(x * planeScale.x, 0, y * planeScale.z);
    pixelPosition.x = -pixelPosition.x; // right handed coordinate system to left handed
    
    float3 rayDirection = normalize(pixelPosition - rays[idx].origin);
    rays[idx].dir = rayDirection;
    rays[idx].tmax   = 100.0f;
}


// Note: uses left handed coordinate system
void createRaysOrthoOnDevice( Ray* rays_device, int width, int height, float3 ray_origin, int rays_per_pixel, float3* instrument_volume_points, Quaternion rotation, float3 planePosition, Quaternion planeRotation, float3 planeScale )
{
    dim3 blockSize( 32, 16 );
    dim3 gridSize( idivCeil( width*rays_per_pixel, blockSize.x ), idivCeil( height, blockSize.y ) );
    createRaysOrthoKernel<<<gridSize, blockSize>>>( rays_device, width, height, ray_origin, rays_per_pixel, instrument_volume_points, rotation, planePosition, planeRotation, planeScale );
}


// __global__ void shadeHitsKernel( char* image, int count, int rays_per_pixel, const Hit* hits, HitReturn* hit_returns, const float3* ray_origins )
__global__ void shadeHitsKernel( char* image, int count, int rays_per_pixel, const Hit* hits )
{
    int idx = threadIdx.x + blockIdx.x * blockDim.x;
    if( idx >= count )
        return;

    int ray_idx = idx * rays_per_pixel;
    image[idx] = 0;
    for (int k = 0; k < rays_per_pixel; ++k)
    {
        if( hits[ray_idx + k].t >= 0.0f )
        {
            ++image[idx];
        }
    }
    
    int gray = 255 - ((255 * image[idx]) / rays_per_pixel);
    image[idx] = (char)gray;
}


// void shadeHitsOnDevice( char* image_device, int count, int rays_per_pixel, const Hit* hits_device, HitReturn* hit_returns_device, const float3* ray_origins_device )
void shadeHitsOnDevice( char* image_device, int count, int rays_per_pixel, const Hit* hits_device )
{
    const int blockSize  = 512;
    const int blockCount = idivCeil( count, blockSize );
    // shadeHitsKernel<<<blockCount, blockSize>>>( image_device, count, rays_per_pixel, hits_device, hit_returns_device, ray_origins_device );
    shadeHitsKernel<<<blockCount, blockSize>>>( image_device, count, rays_per_pixel, hits_device );
}

