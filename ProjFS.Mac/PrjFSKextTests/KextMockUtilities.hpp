#pragma once

#include <unordered_map>
#include <unordered_set>

namespace KextMock
{
    struct PlaceholderValue
    {};
    
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
        uint64_t callSequenceNumber;
        std::unique_ptr<ArgumentValueTuple> argumentValues;
    };
    
    typedef std::unordered_multimap<FunctionPointerType, FunctionCall> RecordedCallMap;

    RecordedCallMap recordedCalls;
    static SpecificFunctionCallRecorder functionTypeRegister;

    void RecordFunctionCall(FunctionPointerType function, uint64_t sequenceNumber, std::unique_ptr<ArgumentValueTuple>&& argumentValues);
    bool DidCallFunction(FunctionPointerType function);
    template <typename... CHECK_ARGS>
        bool DidCallFunction(FunctionPointerType function, CHECK_ARGS... checkArgs);

    virtual void Clear() override
    {
        this->recordedCalls.clear();
    }
    
    friend class MockCalls;
};

template <typename R, typename... ARGS>
    SpecificFunctionCallRecorder<R, ARGS...> SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister;

template <typename R, typename... ARGS>
    void SpecificFunctionCallRecorder<R, ARGS...>::RecordFunctionCall(FunctionPointerType function, uint64_t sequenceNumber, std::unique_ptr<ArgumentValueTuple>&& argumentValues)
{
    this->recordedCalls.insert(std::make_pair(function, FunctionCall { sequenceNumber, std::move(argumentValues) }));
}

template <typename R, typename... ARGS>
    bool SpecificFunctionCallRecorder<R, ARGS...>::DidCallFunction(FunctionPointerType function)
{
    typename RecordedCallMap::const_iterator foundCall = this->recordedCalls.find(function);
    return foundCall != this->recordedCalls.end();
}


class MockCalls
{
    std::unordered_set<FunctionCallRecorder*> functionTypeCallRecorders;
    uint64_t nextCallSequenceNumber = 0;
    
    static MockCalls singleton;
    
public:
    template <typename R, typename... ARGS, typename... ACTUALARGS>
        static void RecordFunctionCall(R (*fn)(ARGS...), ACTUALARGS... args)
    {
        singleton.functionTypeCallRecorders.insert(&SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister);
        
        // Makes a copy of all arguments (even if they were passed by reference; pointers stay pointers, however)
        typedef std::tuple<typename std::remove_reference<ARGS>::type...> ArgumentValueTuple;
        std::unique_ptr<ArgumentValueTuple> argumentValues(std::make_unique<ArgumentValueTuple>(args...));
        
        SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister.RecordFunctionCall(fn, singleton.nextCallSequenceNumber++, std::move(argumentValues));
    }
    

    static void Clear();
    
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
};

template <typename ARG_T, typename CHECK_T>
bool CheckArgument(ARG_T& argument, CHECK_T check)
{
    return argument == check;
}

template <typename ARG_T>
bool CheckArgument(ARG_T& argument, KextMock::PlaceholderValue)
{
    return true;
}

template <typename TUPLE_T, typename CHECK_TUPLE_T, size_t... INDICES>
bool CheckArguments(TUPLE_T& arguments, CHECK_TUPLE_T check, std::index_sequence<INDICES...>)
{
    return (CheckArgument(std::get<INDICES>(arguments), std::get<INDICES>(check)) && ...);
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
            if (CheckArguments(*foundCall->second.argumentValues, std::forward_as_tuple(checkArgs...), std::index_sequence_for<CHECK_ARGS...>{}))
            {
                return true;
            }
        }
    }
    return false;
}
