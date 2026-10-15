/* Exercise release collector code without disabling the test assertions. */
#ifdef NDEBUG
#error Compile contract assertions enabled; use TCMS_CONTRACT_RELEASE instead.
#endif
#ifdef TCMS_CONTRACT_RELEASE
#define NDEBUG
#endif
#include "../../src/NetWasm.Runtime/collector/compact/tcms.c"
#ifdef TCMS_CONTRACT_RELEASE
#undef NDEBUG
#endif
#include <assert.h>
