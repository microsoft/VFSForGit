#pragma once

template<typename T, size_t size>
    constexpr size_t Array_Size(T (&array)[size])
{
    return size;
}

template <typename T> void Array_CopyElements(T* destination, const T* source, size_t count)
{
    for (size_t i = 0; i < count; ++i)
    {
        destination[i] = source[i];
    }
}

template <typename T> void Array_DefaultInit(T* array, size_t count)
{
    for (size_t i = 0; i < count; ++i)
    {
        array[i] = T();
    }
}

