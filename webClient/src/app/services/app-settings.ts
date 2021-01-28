import { environment } from './../../environments/environment';
export interface AppSettings {
	chatEndpoint: string;
}

export function defaultAppSettings(): AppSettings {
	return {
		chatEndpoint: environment.chatEndpoint
	};
}
