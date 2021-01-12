import { Router } from '@angular/router';
import { Observable, Subscription } from 'rxjs';
import { ChatClientService } from './../services/chat-client.service';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { chat } from 'src/protos';

@Component({
	selector: 'app-room',
	templateUrl: './room.component.html',
	styleUrls: ['./room.component.scss']
})
export class RoomComponent implements OnInit, OnDestroy {
	name: Observable<string>;
	channelName: Observable<string>;
	entryMessage: string;
	history: string[] = [];

	private m_MessageSubscription: Subscription;

	constructor(
		private m_ChatClient: ChatClientService,
		private m_Router: Router) { }

	ngOnInit(): void {
		if (!this.m_ChatClient.isConnected) {
			this.m_Router.navigate(['/']);
		}

		this.name = this.m_ChatClient.name;
		this.channelName = this.m_ChatClient.channelName;

		this.m_MessageSubscription = this.m_ChatClient.receiver.subscribe(this.onReceive.bind(this));
	}

	ngOnDestroy(): void {
		this.m_MessageSubscription.unsubscribe();
		this.m_MessageSubscription = null;
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
