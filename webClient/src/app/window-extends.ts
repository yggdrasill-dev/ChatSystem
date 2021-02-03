import PromiseSource from "promise-cs";
import { AppSettings } from "./services/app-settings";

let source = new PromiseSource<AppSettings>();

const configTask = source.promise;

export async function getConfig(): Promise<AppSettings> {
	return await configTask;
}

export function setConfig(settings: AppSettings): void {
	source.resolve(settings);
}
