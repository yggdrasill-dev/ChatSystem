import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, ReplaySubject, Subject } from 'rxjs';
import { chat } from '../../protos/bundle';

@Injectable({
	providedIn: 'root'
})
export class ChatClientService {
	private m_Socket: WebSocket;
	private m_Encoder = new TextEncoder();
	private m_Decoder = new TextDecoder();
	private m_Name: BehaviorSubject<string> = new BehaviorSubject<string>("");
	private m_ChannelName: BehaviorSubject<string> = new BehaviorSubject<string>("");
	private m_ReceiveMessages: Subject<string> = new Subject<string>();

	constructor() { }

	get name(): Observable<string> {
		return this.m_Name;
	}

	get channelName(): Observable<string> {
		return this.m_ChannelName;
	}

	get isConnected(): boolean {
		return this.m_Socket?.readyState == WebSocket.OPEN;
	}

	get receiver(): Observable<string> {
		return this.m_ReceiveMessages;
	}

	open(name: string, channelName: string): Promise<void> {
		return new Promise<void>(
			(resolve, reject) => {
				this.m_Socket = new WebSocket('wss://localhost:5001/ws');
				this.m_Name.next(name);
				this.m_ChannelName.next(channelName);

				this.m_Socket.onmessage = async (ev: MessageEvent<Blob>) => {
					try {
						const buffer = await ev.data.arrayBuffer();

						const msg = chat.ChatMessage.decode(new Uint8Array(buffer));
						const text = this.m_Decoder.decode(msg.payload);

						this.m_ReceiveMessages.next(text);
					}
					catch (ex) {
						console.error(ex);
					}
				};
				this.m_Socket.onopen = () => {
					resolve();
				};
			});
	}
	send(subject: string, message: string | Uint8Array): void {
		if (typeof message === "string") {
			const msg = chat.ChatMessage.create({
				subject,
				payload: this.m_Encoder.encode(message)
			});

			this.m_Socket.send(chat.ChatMessage.encode(msg).finish());
		}

		if (message instanceof Uint8Array) {
			const msg = chat.ChatMessage.create({
				subject,
				payload: message
			});

			this.m_Socket.send(chat.ChatMessage.encode(msg).finish());
		}
	}
}
