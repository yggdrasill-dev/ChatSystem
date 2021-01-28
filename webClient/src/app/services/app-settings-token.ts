import { AppSettings, defaultAppSettings } from './app-settings';
import { InjectionToken } from "@angular/core";

export const AppSettingsToken = new InjectionToken<AppSettings>(
	'AppSettings',
	{
		providedIn: 'platform',
		factory: defaultAppSettings
	});
