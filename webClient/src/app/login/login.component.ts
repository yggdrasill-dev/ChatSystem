import { ChatClientService } from './../services/chat-client.service';
import { Component, Input, OnInit, Output } from '@angular/core';
import { chat } from '../../protos/chat';
import { ActivatedRoute, Router } from '@angular/router';

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

		this.m_Router.navigate(['/room']);
	}
}
