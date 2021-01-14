import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { filter, map } from 'rxjs/operators';
import { ChatClientService } from './../services/chat-client.service';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { chat } from 'src/protos';

@Component({
	selector: 'app-room',
	templateUrl: './room.component.html',
	styleUrls: ['./room.component.scss']
})
export class RoomComponent implements OnInit, OnDestroy {
	name: string;
	channelName: string;
	entryMessage: string;
	history: string[] = [];

	private m_Encoder = new TextEncoder();
	private m_Decoder = new TextDecoder();
	private m_MessageSubscriptions: Subscription[] = [];

	constructor(
		private m_ChatClient: ChatClientService,
		private m_Router: Router) { }

	async ngOnInit(): Promise<void> {
		// this.name = this.m_ChatClient.name;
		// this.channelName = this.m_ChatClient.channelName;

		this.m_MessageSubscriptions.push(
			this.m_ChatClient.receiver
				.pipe(
					filter(msg => msg.subject == 'connect.login.reply'),
					map(msg => {
						const reply = chat.LoginReply.decode(msg.payload);

						return {
							name: reply.name,
							room: reply.room
						};
					})
				)
				.subscribe(msg => {
					this.name = msg.name;
					this.channelName = msg.room;
				})
		);

		this.m_MessageSubscriptions.push(
			this.m_ChatClient.receiver
				.pipe(
					filter(msg => msg.subject == 'connect.receive'),
					map(msg => {
						const content = chat.ChatContent.decode(msg.payload);

						return JSON.stringify(content);
					})
				)
				.subscribe(this.onReceive.bind(this))
		);

		await this.m_ChatClient.open();
	}

	ngOnDestroy(): void {
		for (let sub of this.m_MessageSubscriptions)
			sub.unsubscribe();

		delete this.m_MessageSubscriptions;
	}

	keypress(event: KeyboardEvent): void {
		if (event.key != "Enter" /* Return */)
			return;

		let message = this.entryMessage;

		if (message?.length > 0) {
			let packet = chat.ChatContent.create({
				scope: chat.Scope.ROOM,
				message
			});

			this.m_ChatClient.send("chat.send", chat.ChatContent.encode(packet).finish());
		}

		this.entryMessage = "";
	}
	private onReceive(msg: string): void {
		console.log(this.history);
		this.history?.push(msg);

		if (msg.length > 1000)
			this.history.pop();
	}
}
