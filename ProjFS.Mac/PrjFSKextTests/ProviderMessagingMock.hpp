#include <functional>

void ProvidermessageMock_ResetResultCount();
void ProviderMessageMock_SetDefaultRequestResult(bool success);

void ProviderMessageMock_SetRequestSideEffect(std::function<void()> sideEffectFunction);
void ProviderMessageMock_SetSecondRequestResult(bool secondRequestResult);
