import { Injectable } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { chat } from '../../protos/bundle';

@Injectable({
	providedIn: 'root'
})
export class ChatClientService {
	private m_Socket: WebSocket;
	private m_Encoder = new TextEncoder();
	private m_Decoder = new TextDecoder();
	private m_ReceiveMessages: Subject<chat.IPacket> = new Subject<chat.IPacket>();

	constructor() { }

	get isConnected(): boolean {
		return this.m_Socket?.readyState == WebSocket.OPEN;
	}

	get receiver(): Observable<chat.IPacket> {
		return this.m_ReceiveMessages;
	}

	open(): Promise<void> {
		return new Promise<void>(
			(resolve, reject) => {
				this.m_Socket = new WebSocket('wss://localhost:17002/ws');

				this.m_Socket.onmessage = async (ev: MessageEvent<Blob>) => {
					try {
						const buffer = await ev.data.arrayBuffer();

						const msg = chat.Packet.decode(new Uint8Array(buffer));

						this.m_ReceiveMessages.next(msg);
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
			const msg = chat.Packet.create({
				subject,
				payload: this.m_Encoder.encode(message)
			});

			this.m_Socket.send(chat.Packet.encode(msg).finish());
		}

		if (message instanceof Uint8Array) {
			const msg = chat.Packet.create({
				subject,
				payload: message
			});

			this.m_Socket.send(chat.Packet.encode(msg).finish());
		}
	}
}
