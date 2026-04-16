#pragma once

typedef struct _RaycastingOutput
{
    int arraySize = 0;
    int width = 0;
    int height = 0;
    char* array = nullptr;
    Hit* hits = nullptr;

    // we'll try to return information like RaycastHit to use the same methods 
    // as Unity built-in solution to color the objects
    // HitReturn* hits = nullptr;

} RaycastingOutput;