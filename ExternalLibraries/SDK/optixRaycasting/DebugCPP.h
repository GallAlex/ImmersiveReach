#pragma once
#include<stdio.h>
#include <string>
#include <cstring>
#include <stdio.h>
#include <sstream>

#if defined(_WIN32) || defined(__WIN32__) || defined(__WINDOWS__)
#define DLLExport __declspec(dllexport)
#else
#define DLLExport
#endif

extern "C"
{
    //Create a callback delegate
    typedef void(*FuncCallBack)(const char* message, int color, int size, bool error);
    static FuncCallBack callbackInstance = nullptr;
    DLLExport void RegisterDebugCallback(FuncCallBack cb);
}

//Color Enum
enum class Color { Red, Green, Blue, Black, White, Yellow, Orange };

class  Debug
{
public:
    static void Log(const char* message, Color color = Color::White);
    static void Log(const std::string message, Color color = Color::White);
    static void Log(const int message, Color color = Color::White);
    static void Log(const char message, Color color = Color::White);
    static void Log(const float message, Color color = Color::White);
    static void Log(const double message, Color color = Color::White);
    static void Log(const bool message, Color color = Color::White);
    static void LogError(const char* message, Color color = Color::White);
    static void LogError(const std::string message, Color color = Color::White);
    static void LogError(const int message, Color color = Color::White);
    static void LogError(const char message, Color color = Color::White);
    static void LogError(const float message, Color color = Color::White);
    static void LogError(const double message, Color color = Color::White);
    static void LogError(const bool message, Color color = Color::White);

private:
    static void send_log(const std::stringstream &ss, const Color &color, const bool error = false);
};