import { createBrowserRawExportSession } from "./browser-raw-export-session.mjs";
import { createPageSessionFactory } from "./page-session.mjs";

export const createPageSession = createPageSessionFactory(createBrowserRawExportSession);
