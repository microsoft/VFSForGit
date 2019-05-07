#include "KextMockUtilities.hpp"

MockCalls MockCalls::singleton;
namespace KextMock
{
    PlaceholderValue _;
}

void MockCalls::Clear()
{
    singleton.nextCallSequenceNumber = 0;
    for (FunctionCallRecorder* typeRegister : singleton.functionTypeCallRecorders)
    {
        typeRegister->Clear();
    }
}
