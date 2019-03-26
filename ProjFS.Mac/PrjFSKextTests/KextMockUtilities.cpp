#include "KextMockUtilities.hpp"

MockCalls MockCalls::singleton;

void MockCalls::Clear()
{
    singleton.nextCallSequenceNumber = 0;
    for (FunctionCallRecorder* typeRegister : singleton.functionTypeCallRecorders)
    {
        typeRegister->Clear();
    }
}
