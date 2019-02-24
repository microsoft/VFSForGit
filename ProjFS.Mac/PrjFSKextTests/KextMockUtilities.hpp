#pragma once

#include <unordered_map>
#include <unordered_set>

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
    struct FunctionCall
    {
        // TODO: add argument values
        uint64_t callSequenceNumber;
    };
    
    typedef std::unordered_multimap<FunctionPointerType, FunctionCall> RecordedCallMap;

    RecordedCallMap recordedCalls;
    static SpecificFunctionCallRecorder functionTypeRegister;

    void RecordFunctionCall(FunctionPointerType function, uint64_t sequenceNumber);
    bool DidCallFunction(FunctionPointerType function);
    
    virtual void Clear() override
    {
        this->recordedCalls.clear();
    }
    
    friend class MockCalls;
};

template <typename R, typename... ARGS>
    SpecificFunctionCallRecorder<R, ARGS...> SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister;

template <typename R, typename... ARGS>
    void SpecificFunctionCallRecorder<R, ARGS...>::RecordFunctionCall(FunctionPointerType function, uint64_t sequenceNumber)
{
    this->recordedCalls.insert(std::make_pair(function, FunctionCall { sequenceNumber }));
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
    template <typename R, typename... ARGS>
        static void RecordFunctionCall(R (*fn)(ARGS...), ARGS... args)
    {
        singleton.functionTypeCallRecorders.insert(&SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister);
        
        SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister.RecordFunctionCall(fn, singleton.nextCallSequenceNumber++);
    }
    
    static void Clear();
    
    template <typename R, typename... ARGS>
        static bool DidCallFunction(R (*fn)(ARGS...))
    {
        return SpecificFunctionCallRecorder<R, ARGS...>::functionTypeRegister.DidCallFunction(fn);
    }
};

