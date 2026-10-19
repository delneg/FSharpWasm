import { open } from "./browser/NetWasmProject.mjs";

const app = await open();
app.greet("browser");
await app.close();
