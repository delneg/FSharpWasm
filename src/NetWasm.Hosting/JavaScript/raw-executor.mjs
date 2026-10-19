import {
  commandExecutionContract,
  processExecutionContract,
} from "./execution-contracts.mjs";
import { executeCommand } from "./command-executor.mjs";
import { createExecutionScopeCloser } from "./execution-scope-closer.mjs";
import { failedExecutionResult } from "./execution-result.mjs";
import { createGuestWakeNotifier } from "./guest-wake-notifier.mjs";
import { observeManagedProcess } from "./managed-process-observer.mjs";
import { createPollableReactor } from "./pollable-reactor.mjs";
import { buildRawAbiPlan } from "./raw-abi-plan-builder.mjs";
import {
  bindRawCanonicalImportInstance,
  closeRawCanonicalImportBinding,
  createRawCanonicalImportBinding,
} from "./raw-canonical-import-binding.mjs";
import { bindRawExecutionInstance } from "./raw-execution-instance-binder.mjs";
import { composeRawImports } from "./raw-import-composer.mjs";
import { createTerminalManagedExport } from "./terminal-managed-export.mjs";

const opaqueReactorFailure = Object.freeze({});

export async function executeRaw(request = {}) {
  if (request === null || typeof request !== "object" || Array.isArray(request)) {
    throw new TypeError("raw execution request is required");
  }
  const {
    contractKey,
    abi,
    adapter,
    module,
    providers,
    physicalProviders,
    instantiate,
    bindInstance = () => {},
    consumeTerminalEvent = () => undefined,
    drainTerminalReports = async () => {},
    subscribeInteropCompletion = () => () => {},
    signal = null,
    instanceReleaseActions = [],
    releaseActions = [],
    schedule = globalThis.queueMicrotask?.bind(globalThis),
  } = request;
  if (contractKey !== commandExecutionContract && contractKey !== processExecutionContract) {
    throw new TypeError("raw execution contract is unsupported");
  }
  if (module === null || typeof module !== "object") {
    throw new TypeError("verified raw module is required");
  }
  if (abi === null || typeof abi !== "object" || Array.isArray(abi)) {
    throw new TypeError("raw execution ABI is required");
  }
  if (adapter === null || typeof adapter !== "object" || Array.isArray(adapter)) {
    throw new TypeError("generated raw adapter is required");
  }
  if (providers === null || typeof providers !== "object" || Array.isArray(providers)) {
    throw new TypeError("raw logical providers are required");
  }
  if (physicalProviders === null || typeof physicalProviders !== "object"
      || Array.isArray(physicalProviders)) {
    throw new TypeError("raw physical providers are required");
  }
  if (typeof instantiate !== "function") {
    throw new TypeError("raw module instantiator is required");
  }
  if (typeof bindInstance !== "function") {
    throw new TypeError("raw instance binding action is required");
  }
  if (typeof consumeTerminalEvent !== "function"
      || typeof drainTerminalReports !== "function") {
    throw new TypeError("raw terminal exception actions are required");
  }
  validateSignal(signal);
  if (typeof subscribeInteropCompletion !== "function") {
    throw new TypeError("raw interop completion subscription is required");
  }
  if (typeof schedule !== "function") {
    throw new TypeError("raw execution scheduler is required");
  }
  const closeInstanceScope = createExecutionScopeCloser(instanceReleaseActions);
  const closeCallerScope = createExecutionScopeCloser(releaseActions);

  if (contractKey === commandExecutionContract) {
    return executeRawCommand({
      contractKey,
      abi,
      adapter,
      module,
      providers,
      physicalProviders,
      instantiate,
      bindInstance,
      consumeTerminalEvent,
      drainTerminalReports,
      signal,
      closeInstanceScope,
      closeCallerScope,
    });
  }
  return executeRawProcess({
    subscribeInteropCompletion,
    contractKey,
    abi,
    adapter,
    module,
    providers,
    physicalProviders,
    instantiate,
    bindInstance,
    drainTerminalReports,
    signal,
    closeInstanceScope,
    closeCallerScope,
    schedule,
  });
}

async function executeRawCommand({
  contractKey,
  abi,
  adapter,
  module,
  providers,
  physicalProviders,
  instantiate,
  bindInstance,
  consumeTerminalEvent,
  drainTerminalReports,
  signal,
  closeInstanceScope,
  closeCallerScope,
}) {
  let plan;
  try {
    plan = buildRawAbiPlan({ contractKey, abi });
  } catch (error) {
    return closeOwnedScopes(rawAbiFailure(error), closeInstanceScope, closeCallerScope);
  }
  let canonicalBinding;
  try {
    canonicalBinding = createRawCanonicalImportBinding({ adapter, providers, reactor: null });
  } catch (error) {
    return closeOwnedScopes(rawAbiFailure(error), closeInstanceScope, closeCallerScope);
  }
  const closeCanonicalScope = createExecutionScopeCloser([
    canonicalReleaseAction(canonicalBinding),
  ]);
  const closeScope = outcome => closeOwnedScopes(
    outcome,
    closeInstanceScope,
    closeCanonicalScope,
    closeCallerScope);
  let imports;
  try {
    imports = composeRawImports({
      canonicalBinding,
      inventory: importInventory(plan),
      physicalProviders,
    });
  } catch (error) {
    return closeScope(rawAbiFailure(error));
  }
  if (signal?.aborted) {
    return closeScope(preInstantiationCancellation());
  }
  let instance;
  try {
    instance = await instantiate(Object.freeze({ module, imports }));
  } catch (error) {
    return closeScope(rawInstantiationFailure(error));
  }
  try {
    bindInstance(instance);
  } catch (error) {
    return closeScope(rawInstanceBindingFailure(error));
  }
  let binding;
  try {
    bindRawCanonicalImportInstance({ binding: canonicalBinding, instance });
    binding = bindRawExecutionInstance({ instance, plan });
  } catch (error) {
    return closeScope(rawExportFailure(error));
  }
  const run = createTerminalManagedExport(
    "run",
    binding.command.run,
    consumeTerminalEvent,
    () => {
      const clearActiveException = instance.exports.exception_clear_active;
      if (typeof clearActiveException !== "function") {
        throw new TypeError("raw managed exception cleanup export is unavailable");
      }
      clearActiveException.call(instance.exports);
    });
  const outcome = executeCommand({ run, signal });
  await drainTerminalReports();
  return closeScope(outcome);
}

async function executeRawProcess({
  subscribeInteropCompletion,
  contractKey,
  abi,
  adapter,
  module,
  providers,
  physicalProviders,
  instantiate,
  bindInstance,
  drainTerminalReports,
  signal,
  closeInstanceScope,
  closeCallerScope,
  schedule,
}) {
  let guestEntryEnabled = false;
  let initializationReactorUse = false;
  let observeWake;
  let notifier;
  const reactor = createPollableReactor({
    onReady: token => notifier.notify(token),
    onFailure: () => observeWake(opaqueReactorFailure),
    schedule,
  });
  const closeReactorScope = createExecutionScopeCloser([
    Object.freeze({
      code: "host.raw-reactor",
      message: "The execution host could not close the raw reactor.",
      release: reactor.close,
    }),
  ]);
  const closeBaseScope = outcome => closeOwnedScopes(
    outcome,
    closeReactorScope,
    closeInstanceScope,
    closeCallerScope);

  function assertGuestEntry() {
    if (guestEntryEnabled) return;
    initializationReactorUse = true;
    throw new Error("raw reactor use is unavailable before guest entry");
  }

  let plan;
  try {
    plan = buildRawAbiPlan({ contractKey, abi });
  } catch (error) {
    return closeBaseScope(rawAbiFailure(error));
  }
  let canonicalBinding;
  try {
    canonicalBinding = createRawCanonicalImportBinding({
      adapter,
      providers,
      reactor: Object.freeze({
        assertAvailable: assertGuestEntry,
        cancel: reactor.cancel,
        module: plan.reactorHostModule,
        watch: reactor.watch,
      }),
    });
  } catch (error) {
    return closeBaseScope(rawAbiFailure(error));
  }
  const closeCanonicalScope = createExecutionScopeCloser([
    canonicalReleaseAction(canonicalBinding),
  ]);
  const closeScope = outcome => closeOwnedScopes(
    outcome,
    closeReactorScope,
    closeInstanceScope,
    closeCanonicalScope,
    closeCallerScope);
  let imports;
  try {
    imports = composeRawImports({
      canonicalBinding,
      inventory: importInventory(plan),
      physicalProviders,
    });
  } catch (error) {
    return closeScope(rawAbiFailure(error));
  }
  if (signal?.aborted) {
    return closeScope(preInstantiationCancellation());
  }

  let instance;
  try {
    instance = await instantiate(Object.freeze({ module, imports }));
  } catch (error) {
    return closeScope(initializationReactorUse
      ? reactorInitializationFailure(error)
      : rawInstantiationFailure(error));
  }
  if (initializationReactorUse) {
    return closeScope(reactorInitializationFailure());
  }

  try {
    bindInstance(instance);
  } catch (error) {
    return closeScope(rawInstanceBindingFailure(error));
  }
  let binding;
  try {
    bindRawCanonicalImportInstance({ binding: canonicalBinding, instance });
    binding = bindRawExecutionInstance({ instance, plan });
    notifier = createGuestWakeNotifier({
      guestWake: binding.reactorGuest.wake,
      observeWake: error => observeWake(error),
    });
  } catch (error) {
    return closeScope(rawExportFailure(error));
  }
  guestEntryEnabled = true;
  let outcome;
  try {
    outcome = await observeManagedProcess({
      process: binding.process,
      subscribeWake(observer) {
        observeWake = observer;
        const unsubscribeCompletion = subscribeInteropCompletion(observer);
        return () => { unsubscribeCompletion(); observeWake = undefined; };
      },
      signal,
    });
  } catch (error) {
    outcome = rawExecutionFailure(error);
  } finally {
    guestEntryEnabled = false;
  }
  await drainTerminalReports();
  return closeScope(outcome);
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("raw signal must be an AbortSignal");
  }
}

function importInventory(plan) {
  return Object.freeze({
    target: plan.target,
    imports: plan.imports,
    reactorHostModule: plan.reactorHostModule,
  });
}

function rawAbiFailure(cause) {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.raw-abi",
    "The final raw module does not match its execution ABI, generated adapter, or providers.",
    cause);
}

function canonicalReleaseAction(binding) {
  return Object.freeze({
    code: "host.raw-canonical-imports",
    message: "The execution host could not close the raw canonical imports.",
    release() { closeRawCanonicalImportBinding({ binding }); },
  });
}

function rawInstantiationFailure(cause) {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.raw-instantiate",
    "The execution host could not instantiate the raw module.",
    cause);
}

function rawInstanceBindingFailure(cause) {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.raw-instance-bind",
    "The execution host could not bind services to the raw instance.",
    cause);
}

function rawExecutionFailure(cause) {
  return failedExecutionResult(
    "hostFailure",
    "execution",
    "host.raw-execution",
    "The execution host could not execute the raw module.",
    cause);
}

function rawExportFailure(cause) {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.raw-exports",
    "The instantiated raw module does not expose its declared execution boundary.",
    cause);
}

function reactorInitializationFailure(cause) {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.reactor-initialization",
    "The raw module used the reactor before guest entry.",
    cause);
}

function preInstantiationCancellation() {
  return failedExecutionResult(
    "callerCancellation",
    "instantiation",
    "caller.cancelled",
    "Raw execution was cancelled by the caller before instantiation.");
}

async function closeOwnedScopes(outcome, ...closers) {
  for (const close of closers) outcome = await close(outcome);
  return outcome;
}
