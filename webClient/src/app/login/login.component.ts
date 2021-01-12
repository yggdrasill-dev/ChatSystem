import { ChatClientService } from './../services/chat-client.service';
import { Component, OnInit } from '@angular/core';
import { chat } from '../../protos';
import { Router } from '@angular/router';

@Component({
	selector: 'app-login',
	templateUrl: './login.component.html',
	styleUrls: ['./login.component.scss']
})
export class LoginComponent implements OnInit {
	name: string = '';
	channelName: string = '';

	constructor(
		private m_ChatClient: ChatClientService,
		private m_Router: Router) { }

	ngOnInit(): void {
	}

	async join(): Promise<void> {
		console.info(`Name: ${this.name}, Channel: ${this.channelName}`);

		await this.m_ChatClient.open(this.name, this.channelName);
		// this.m_ChatClient.send("chat.test", "hello test");

		var loginMsg = new chat.LoginRegistration({
			name: this.name,
			channel: this.channelName
		});
		this.m_ChatClient.send("connect.register", chat.LoginRegistration.encode(loginMsg).finish());

		this.m_Router.navigate(['/room']);
	}
}
