#pragma once

template <typename T, size_t N>
    constexpr bool AllArrayElementsInitialized(T (&array)[N], size_t fromIndex = 0)
{
    return
        fromIndex >= N
        ? true
        : (array[fromIndex] != T()
           && AllArrayElementsInitialized(array, fromIndex + 1));
}
