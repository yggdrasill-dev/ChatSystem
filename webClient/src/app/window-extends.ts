import { AsyncSubject } from "rxjs";
import { AppSettings } from "./services/app-settings";

let subject = new AsyncSubject<AppSettings>();

const configTask = subject.toPromise();

export async function getConfig(): Promise<AppSettings> {
	return await configTask;
}

export function setConfig(settings: AppSettings): void {
	subject.next(settings);
	subject.complete();
}
