/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_metadata.h"
#include "../../runtime_libc_initializer.h"

#include <stdlib.h>

void *collector_allocate_metadata(size_t size)
{
    runtime_initialize_libc();
    return calloc(1, size == 0 ? 1 : size);
}

void collector_release_metadata(void *address)
{
    free(address);
}
