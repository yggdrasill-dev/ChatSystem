import { enableProdMode } from '@angular/core';
import { platformBrowserDynamic } from '@angular/platform-browser-dynamic';

import { AppModule } from './app/app.module';
import { defaultAppSettings } from './app/services/app-settings';
import { AppSettingsToken } from './app/services/app-settings-token';
import { getConfig, setConfig } from './app/window-extends';
import { environment } from './environments/environment';

if (environment.production) {
	enableProdMode();
}

window["setConfig"] = setConfig;

getConfig().then(config => {
	let appSettings = Object.assign(defaultAppSettings(), config);

	return platformBrowserDynamic([
		{
			provide: AppSettingsToken,
			useValue: appSettings
		}
	])
		.bootstrapModule(AppModule)
		.catch(err => console.error(err));
});
