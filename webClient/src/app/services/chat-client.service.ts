import { AppSettingsToken } from './app-settings-token';
import { AppSettings } from './app-settings';
import { Observable, Subject } from 'rxjs';
import { chat } from '../../protos/bundle';
import { Inject, Injectable } from '@angular/core';

@Injectable({
	providedIn: 'root'
})
export class ChatClientService {
	private m_Socket: WebSocket;
	private m_Encoder = new TextEncoder();
	private m_Decoder = new TextDecoder();
	private m_ReceiveMessages: Subject<chat.IPacket> = new Subject<chat.IPacket>();

	constructor(@Inject(AppSettingsToken) private m_AppConfig: AppSettings) { }

	get isConnected(): boolean {
		return this.m_Socket?.readyState == WebSocket.OPEN;
	}

	get receiver(): Observable<chat.IPacket> {
		return this.m_ReceiveMessages;
	}

	open(): Promise<void> {
		return new Promise<void>(
			(resolve, reject) => {
				this.m_Socket = new WebSocket(this.m_AppConfig.chatEndpoint);

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

	send(subject: string, message: Uint8Array): void {
		const msg = chat.Packet.create({
			subject,
			payload: message
		});

		this.m_Socket.send(chat.Packet.encode(msg).finish());
	}
}
