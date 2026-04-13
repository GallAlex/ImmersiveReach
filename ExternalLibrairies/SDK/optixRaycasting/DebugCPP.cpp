#include "DebugCPP.h"

#include<stdio.h>
#include <string>
#include <cstring>
#include <stdio.h>
#include <sstream>

//-------------------------------------------------------------------
void  Debug::Log(const char* message, Color color) {
    if (callbackInstance != nullptr)
        callbackInstance(message, (int)color, (int)strlen(message), false);
}

void  Debug::Log(const std::string message, Color color) {
    const char* tmsg = message.c_str();
    if (callbackInstance != nullptr)
        callbackInstance(tmsg, (int)color, (int)strlen(tmsg), false);
}

void  Debug::Log(const int message, Color color) {
    std::stringstream ss;
    ss << message;
    send_log(ss, color);
}

void  Debug::Log(const char message, Color color) {
    std::stringstream ss;
    ss << message;
    send_log(ss, color);
}

void  Debug::Log(const float message, Color color) {
    std::stringstream ss;
    ss << message;
    send_log(ss, color);
}

void  Debug::Log(const double message, Color color) {
    std::stringstream ss;
    ss << message;
    send_log(ss, color);
}

void Debug::Log(const bool message, Color color) {
    std::stringstream ss;
    if (message)
        ss << "true";
    else
        ss << "false";

    send_log(ss, color);
}

void  Debug::LogError(const char* message, Color color) {
    if (callbackInstance != nullptr)
        callbackInstance(message, (int)color, (int)strlen(message), true);
}

void  Debug::LogError(const std::string message, Color color) {
    const char* tmsg = message.c_str();
    if (callbackInstance != nullptr)
        callbackInstance(tmsg, (int)color, (int)strlen(tmsg), true);
}

void  Debug::LogError(const int message, Color color) {
    std::stringstream ss;
    ss << message;
    send_log(ss, color, true);
}

void  Debug::LogError(const char message, Color color) {
    std::stringstream ss;
    ss << message;
    send_log(ss, color, true);
}

void  Debug::LogError(const float message, Color color) {
    std::stringstream ss;
    ss << message;
    send_log(ss, color, true);
}

void  Debug::LogError(const double message, Color color) {
    std::stringstream ss;
    ss << message;
    send_log(ss, color, true);
}

void Debug::LogError(const bool message, Color color) {
    std::stringstream ss;
    if (message)
        ss << "true";
    else
        ss << "false";

    send_log(ss, color, true);
}

void Debug::send_log(const std::stringstream &ss, const Color &color, const bool error) {
    const std::string tmp = ss.str();
    const char* tmsg = tmp.c_str();
    if (callbackInstance != nullptr)
        callbackInstance(tmsg, (int)color, (int)strlen(tmsg), error);
}
//-------------------------------------------------------------------

//Create a callback delegate
void RegisterDebugCallback(FuncCallBack cb) {
    callbackInstance = cb;
}