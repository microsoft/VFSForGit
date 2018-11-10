#pragma once

// Helper templates similar to the C++ standard library's which are missing in the kernel 
namespace KextCpp
{
    template <typename T> struct remove_reference
    {
        typedef T type;
    };
    template <typename T> struct remove_reference<T&>
    {
        typedef T type;
    };
    template <typename T> struct remove_reference<T&&>
    {
        typedef T type;
    };
    
    template <typename T> typename remove_reference<T>::type&& move(T&& o) __attribute__((visibility("hidden")));
    template <typename T> typename remove_reference<T>::type&& move(T&& o)
    {
        return static_cast<typename remove_reference<T>::type&&>(o);
    }
}

