import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { filter, map } from 'rxjs/operators';
import { ChatClientService } from './../services/chat-client.service';
import { AfterViewChecked, Component, OnDestroy, OnInit } from '@angular/core';
import { chat } from 'src/protos';

@Component({
	selector: 'app-room',
	templateUrl: './room.component.html',
	styleUrls: ['./room.component.scss']
})
export class RoomComponent implements OnInit, OnDestroy, AfterViewChecked {
	name: string;
	channelName: string;
	entryMessage: string;
	history: chat.ChatMessage[] = [];
	players: string[] = [];
	sendTarget: string = "*";

	private m_Encoder = new TextEncoder();
	private m_Decoder = new TextDecoder();
	private m_MessageSubscriptions: Subscription[] = [];

	constructor(
		private m_ChatClient: ChatClientService,
		private m_Router: Router) { }

	ngAfterViewChecked(): void {
		document.scrollingElement.scrollTop = document.scrollingElement.scrollHeight;
	}

	async ngOnInit(): Promise<void> {
		if (!this.m_ChatClient.canCommunication()) {
			await this.m_Router.navigate(["/"]);
			return;
		}
		this.name = await this.m_ChatClient.getUserName();
		// this.channelName = this.m_ChatClient.channelName;

		this.m_MessageSubscriptions.push(
			this.m_ChatClient.receiver
				.pipe(
					filter(msg => msg.subject == "room.player.refrash")
				)
				.subscribe(msg => {
					this.m_ChatClient.send("room.player.list", new Uint8Array());
				})
		);

		this.m_MessageSubscriptions.push(
			this.m_ChatClient.receiver
				.pipe(
					filter(msg => msg.subject == 'chat.receive'),
					map(msg => {
						const content = chat.ChatMessage.decode(msg.payload);

						return content;
					})
				)
				.subscribe(this.onReceive.bind(this))
		);

		this.m_MessageSubscriptions.push(
			this.m_ChatClient.receiver
				.pipe(
					filter(msg => msg.subject == 'chat.player.list'),
					map(msg => {
						const reply = chat.PlayerList.decode(msg.payload);

						return reply;
					})
				)
				.subscribe(reply => {
					this.channelName = reply.room;
					this.players = reply.players;
				})
		);

		this.m_ChatClient.send("room.player.list", new Uint8Array());
	}

	ngOnDestroy(): void {
		for (let sub of this.m_MessageSubscriptions)
			sub.unsubscribe();

		delete this.m_MessageSubscriptions;
	}

	keypress(event: KeyboardEvent): void {
		if (event.key != "Enter" /* Return */)
			return;

		let sendTarget = this.sendTarget;
		let message = this.entryMessage;

		if (message?.length > 0) {
			if (sendTarget != '*') {
				let packet = chat.ChatMessage.create({
					scope: chat.Scope.SCOPE_PERSON,
					target: sendTarget,
					message
				});

				this.m_ChatClient.send("chat.send", chat.ChatMessage.encode(packet).finish());
			} else {
				let packet = chat.ChatMessage.create({
					scope: chat.Scope.SCOPE_ROOM,
					message
				});

				this.m_ChatClient.send("chat.send", chat.ChatMessage.encode(packet).finish());
			}
		}

		this.entryMessage = "";
	}
	private onReceive(msg: chat.ChatMessage): void {
		console.log(this.history);
		this.history?.push(msg);

		if (this.history?.length > 1000)
			this.history.pop();
	}

	public formatMessage(msg: chat.ChatMessage): string {
		switch (msg.scope) {
			case chat.Scope.SCOPE_ROOM:
				return `[${msg.from}]: ${msg.message}`;
			case chat.Scope.SCOPE_PERSON:
				return `[${msg.from}] => [${msg.target}]: ${msg.message}`;
			case chat.Scope.SCOPE_SYSTEM:
				return `[System]: ${msg.message}`;
			default:
				return `Receive message failed`;
		}
	}

	public getMessageStyle(msg: chat.ChatMessage): string {
		switch (msg.scope) {
			case chat.Scope.SCOPE_ROOM:
				return 'msg-room';
			case chat.Scope.SCOPE_PERSON:
				return 'msg-person';
			case chat.Scope.SCOPE_SYSTEM:
				return 'msg-system';
			default:
				return 'msg-error';
		}
	}
}
