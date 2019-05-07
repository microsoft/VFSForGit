#pragma once

#include <unordered_map>
#include <unordered_set>

namespace KextMock
{
    struct PlaceholderValue
    {};
    
    // Matches with any parameter value
    // ParameterPlaceholderValue can be used:
    //   - When calling kext functions for parameter values that do not matter to the test
    //   - When checking if a function was called (i.e. DidCallFunction).  ParameterPlaceholderValue
    //     is a match for all parameter values.
    extern PlaceholderValue _;
};

class FunctionCallRecorder
{
public:
    virtual void Clear() = 0;
    virtual ~FunctionCallRecorder() {}
};

template <typename R, typename... ARGS>
    class SpecificFunctionCallRecorder : public FunctionCallRecorder
{
    typedef R (*FunctionPointerType)(ARGS...);
    typedef std::tuple<typename std::remove_reference<ARGS>::type...> ArgumentValueTuple;
    
    struct FunctionCall
    {
        int64_t callSequenceNumber;
        std::unique_ptr<ArgumentValueTuple> argumentValues;
    };
    
    typedef std::unordered_multimap<FunctionPointerType, FunctionCall> RecordedCallMap;

    RecordedCallMap recordedCalls;
    static SpecificFunctionCallRecorder functionTypeRegister;

    void RecordFunctionCall(FunctionPointerType function, int64_t sequenceNumber, std::unique_ptr<ArgumentValueTuple>&& argumentValues);
    bool DidCallFunction(FunctionPointerType function);
    template <typename... CHECK_ARGS>
        bool DidCallFunction(FunctionPointerType function, CHECK_ARGS... checkArgs);
    int CallCount(FunctionPointerType function);
    int64_t EarliestSequenceNumberForCallMatching(FunctionPointerType function, int64_t sequenceNumberGreaterThan);
    template <typename... CHECK_ARGS>
        int64_t EarliestSequenceNumberForCallMatching(FunctionPointerType function, int64_t sequenceNumberGreaterThan, const std::tuple<CHECK_ARGS...>& checkArgTuple);

    virtual void Clear() override
    {
        this->recordedCalls.clear();
    }
    
    friend class MockCalls;
};

template <typename R, typename... ARGS>
    SpecificFunctionCallRecorder<R, ARGS...> SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister;

template <typename R, typename... ARGS>
    void SpecificFunctionCallRecorder<R, ARGS...>::RecordFunctionCall(FunctionPointerType function, int64_t sequenceNumber, std::unique_ptr<ArgumentValueTuple>&& argumentValues)
{
    this->recordedCalls.insert(std::make_pair(function, FunctionCall { sequenceNumber, std::move(argumentValues) }));
}

template <typename R, typename... ARGS>
    bool SpecificFunctionCallRecorder<R, ARGS...>::DidCallFunction(FunctionPointerType function)
{
    return this->EarliestSequenceNumberForCallMatching(function, -1) > -1;
}

template <typename R, typename... ARGS>
    int64_t SpecificFunctionCallRecorder<R, ARGS...>::EarliestSequenceNumberForCallMatching(FunctionPointerType function, int64_t sequenceNumberGreaterThan)
{
    std::pair foundCalls = this->recordedCalls.equal_range(function);
    for (typename RecordedCallMap::const_iterator foundCall = foundCalls.first; foundCall != foundCalls.second; ++foundCall)
    {
        if (foundCall->second.callSequenceNumber > sequenceNumberGreaterThan)
        {
            return foundCall->second.callSequenceNumber;
        }
    }
    
    return sequenceNumberGreaterThan;
}


class MockCalls
{
    std::unordered_set<FunctionCallRecorder*> functionTypeCallRecorders;
    int64_t nextCallSequenceNumber = 0;
    
    static MockCalls singleton;
    
public:
    template <typename R, typename... ARGS, typename... ACTUAL_ARGS>
        static void RecordFunctionCall(R (*fn)(ARGS...), ACTUAL_ARGS... args)
    {
        singleton.functionTypeCallRecorders.insert(&SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister);
        
        // Makes a copy of all arguments (even if they were passed by reference; pointers stay pointers, however)
        typedef std::tuple<typename std::remove_reference<ARGS>::type...> ArgumentValueTuple;
        std::unique_ptr<ArgumentValueTuple> argumentValues(std::make_unique<ArgumentValueTuple>(args...));
        
        SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister.RecordFunctionCall(fn, singleton.nextCallSequenceNumber++, std::move(argumentValues));
    }
    

    static void Clear();
    
    static bool DidCallAnyFunctions()
    {
        return singleton.nextCallSequenceNumber > 0;
    }
    
    template <typename R, typename... ARGS>
        static int CallCount(R (*fn)(ARGS...))
    {
        return SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister.CallCount(fn);
    }
    
    template <typename R, typename... ARGS>
        static bool DidCallFunction(R (*fn)(ARGS...))
    {
        return SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister.DidCallFunction(fn);
    }
    
    template <typename R, typename... ARGS, typename... CHECK_ARGS>
        static bool DidCallFunction(R (*fn)(ARGS...), CHECK_ARGS... checkArgs)
    {
        return SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister.DidCallFunction(fn, checkArgs...);
    }
    
    static bool FunctionCallOrderCheck(int64_t sequenceNumberGreaterThan)
    {
        return true;
    }
    
    template <typename R, typename... ARGS>
        static int64_t EarliestSequenceNumberForCallMatching(int64_t sequenceNumberGreaterThan, R (*fn)(ARGS...))
    {
        return SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister.EarliestSequenceNumberForCallMatching(fn, sequenceNumberGreaterThan);
    }
    
    template <typename R, typename... ARGS, typename... CHECK_ARGS>
        static int64_t EarliestSequenceNumberForCallMatching(int64_t sequenceNumberGreaterThan, R (*fn)(ARGS...), const std::tuple<CHECK_ARGS...>& checkArgTuple)
    {
        return SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister.EarliestSequenceNumberForCallMatching(fn, sequenceNumberGreaterThan, checkArgTuple);
    }
    
    template <typename FN_T, typename... FN_TYPES>
        static bool FunctionCallOrderCheck(int64_t sequenceNumberGreaterThan, FN_T* function1, FN_TYPES... functions)
    {
        int64_t callSequenceNumber = MockCalls::EarliestSequenceNumberForCallMatching(sequenceNumberGreaterThan, function1);
        if (callSequenceNumber > sequenceNumberGreaterThan)
        {
            return FunctionCallOrderCheck(callSequenceNumber, functions...);
        }
        else
        {
            return false;
        }
    }
    
    template <typename FN_T, typename... CHECK_ARGS, typename... FN_TYPES>
        static bool FunctionCallOrderCheck(
            int64_t sequenceNumberGreaterThan,
            FN_T* function1,
            const std::tuple<CHECK_ARGS...>& function1ArgumentTuple,
            FN_TYPES... functions)
    {
        int64_t callSequenceNumber = MockCalls::EarliestSequenceNumberForCallMatching(sequenceNumberGreaterThan, function1, function1ArgumentTuple);
        if (callSequenceNumber > sequenceNumberGreaterThan)
        {
            return FunctionCallOrderCheck(callSequenceNumber, functions...);
        }
        else
        {
            return false;
        }
    }

    template <typename FN_T, typename... CHECK_ARGS, typename... FN_TYPES>
        static bool FunctionCallOrderCheck(
            int64_t sequenceNumberGreaterThan,
            FN_T* function1,
            KextMock::PlaceholderValue,
            FN_TYPES... functions)
    {
        int64_t callSequenceNumber = MockCalls::EarliestSequenceNumberForCallMatching(sequenceNumberGreaterThan, function1);
        if (callSequenceNumber > sequenceNumberGreaterThan)
        {
            return FunctionCallOrderCheck(callSequenceNumber, functions...);
        }
        else
        {
            return false;
        }
    }

    template <typename... FN_TYPES>
        static bool DidCallFunctionsInOrder(FN_TYPES... functions)
    {
        return MockCalls::FunctionCallOrderCheck(-1, functions...);
    }
};

template <typename ARG_T, typename CHECK_T>
bool ArgumentIsEqual(ARG_T& argument, CHECK_T check)
{
    return argument == check;
}

template <typename ARG_T>
bool ArgumentIsEqual(ARG_T& argument, KextMock::PlaceholderValue)
{
    return true;
}

template <typename TUPLE_T, typename CHECK_TUPLE_T, size_t... INDICES>
bool ArgumentsAreEqual(TUPLE_T& arguments, CHECK_TUPLE_T check, std::index_sequence<INDICES...>)
{
    return (ArgumentIsEqual(std::get<INDICES>(arguments), std::get<INDICES>(check)) && ...);
}

template <typename R, typename... ARGS>
template <typename... CHECK_ARGS>
    bool SpecificFunctionCallRecorder<R, ARGS...>::DidCallFunction(FunctionPointerType function, CHECK_ARGS... checkArgs)
{
    std::pair<typename RecordedCallMap::const_iterator, typename RecordedCallMap::const_iterator> foundCalls = this->recordedCalls.equal_range(function);
    for (typename RecordedCallMap::const_iterator foundCall = foundCalls.first; foundCall != foundCalls.second; ++foundCall)
    {
        if (foundCall->second.argumentValues)
        {
            if (ArgumentsAreEqual(*foundCall->second.argumentValues, std::forward_as_tuple(checkArgs...), std::index_sequence_for<CHECK_ARGS...>{}))
            {
                return true;
            }
        }
    }
    return false;
}

template <typename R, typename... ARGS>
int SpecificFunctionCallRecorder<R, ARGS...>::CallCount(FunctionPointerType function)
{
    std::pair<typename RecordedCallMap::const_iterator, typename RecordedCallMap::const_iterator> foundCalls = this->recordedCalls.equal_range(function);
    return static_cast<int>(std::distance(foundCalls.first, foundCalls.second));
}

template <typename R, typename... ARGS>
template <typename... CHECK_ARGS>
int64_t SpecificFunctionCallRecorder<R, ARGS...>::EarliestSequenceNumberForCallMatching(FunctionPointerType function, int64_t sequenceNumberGreaterThan, const std::tuple<CHECK_ARGS...>& checkArgTuple)
{
    std::pair<typename RecordedCallMap::const_iterator, typename RecordedCallMap::const_iterator> foundCalls = this->recordedCalls.equal_range(function);
    for (typename RecordedCallMap::const_iterator foundCall = foundCalls.first; foundCall != foundCalls.second; ++foundCall)
    {
        if (foundCall->second.argumentValues && foundCall->second.callSequenceNumber > sequenceNumberGreaterThan)
        {
            if (ArgumentsAreEqual(*foundCall->second.argumentValues, checkArgTuple, std::index_sequence_for<CHECK_ARGS...>{}))
            {
                return foundCall->second.callSequenceNumber;
            }
        }
    }
    
    return sequenceNumberGreaterThan;
}
