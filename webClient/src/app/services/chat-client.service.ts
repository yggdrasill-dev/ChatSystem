import { AsyncSubject } from 'rxjs';
import { AppSettingsToken } from './app-settings-token';
import { AppSettings } from './app-settings';
import { Observable, Subject, Subscription } from 'rxjs';
import { chat } from '../../protos/bundle';
import { Inject, Injectable, OnDestroy } from '@angular/core';
import { interpret, Machine, Interpreter } from 'xstate';
import { filter } from 'rxjs/operators';
import PromiseSource from 'promise-cs';

interface ChatBehavior {
	open(): Promise<string>;
	send(subject: string, message: Uint8Array): void;
	listRoom(): Promise<chat.IRoom[]>;
	joinRoom(room: string, password?: string): Promise<void>;
	canCommunication(): boolean;
}

function buildContext(): ChatBehavior {
	return {
		open: async () => {
			throw "current state can't open";
		},
		send: () => {
			throw "current state can't send";
		},
		listRoom: async () => {
			throw "current state can't listRoom";
		},
		joinRoom: () => {
			throw "current state can't joinRoom";
		},
		canCommunication: () => false
	};
}

class ChatOperator {
	private m_Socket: WebSocket;
	private m_Encoder = new TextEncoder();
	private m_Decoder = new TextDecoder();
	private m_ReceiveMessages: Subject<chat.IPacket> = new Subject<chat.IPacket>();

	constructor(private m_AppConfig: AppSettings) {
	}

	get receiver(): Observable<chat.IPacket> {
		return this.m_ReceiveMessages;
	}

	open(): Promise<string> {
		return new Promise<string>(
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
					let subscription = this.receiver
						.pipe(
							filter(msg => msg.subject == 'connect.login.reply')
						)
						.subscribe(msg => {
							const reply = chat.LoginReply.decode(msg.payload);

							if (reply.status == chat.LoginStatus.LOGINSTATUS_ACCPET) {
								resolve(reply.name)
							}
							else {
								reject('login fail');
							}

							subscription.unsubscribe();
						});
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

	listRoom(): Promise<chat.IRoom[]> {
		this.send("room.list", new Uint8Array());

		let source = new PromiseSource<chat.IRoom[]>();

		let subscription = this.receiver
			.pipe(
				filter(msg => msg.subject == 'chat.room.list')
			)
			.subscribe(msg => {
				const reply = chat.RoomList.decode(msg.payload);

				source.resolve(reply.rooms);
			});

		return source.promise.finally(() => {
			subscription.unsubscribe();
		});
	}

	joinRoom(room: string, password?: string): Promise<void> {
		let source = new PromiseSource<void>();

		let subscription = this.receiver
			.pipe(
				filter(msg => msg.subject == 'room.join.reply')
			)
			.subscribe(msg => {
				const reply = chat.JoinRoomReply.decode(msg.payload);

				if (reply.status == chat.JoinRoomStatus.JOINROOMSTATUS_ACCPET)
					source.resolve();
				else
					source.reject('進房失敗');
			});

		this.send(
			'room.join',
			chat.JoinRoom.encode({
				room,
				password
			}).finish());

		return source.promise.finally(() => {
			subscription.unsubscribe();
		});
	}
}

@Injectable({
	providedIn: 'root'
})
export class ChatClientService implements OnDestroy {
	private m_Operator: ChatOperator;
	private m_ConnectionService: Interpreter<
		ChatBehavior,
		any,
		any,
		{ value: any; context: ChatBehavior }>;
	private m_LoginNameSubject: AsyncSubject<string> = new AsyncSubject<string>();

	constructor(@Inject(AppSettingsToken) private m_AppConfig: AppSettings) {
		this.m_Operator = new ChatOperator(this.m_AppConfig);

		const connectionMachine = Machine<ChatBehavior, any>({
			id: 'Connection',
			initial: 'init',
			states: {
				init: {
					entry: (context) => {
						Object.assign(
							context,
							buildContext(),
							{
								open: () => this.m_Operator.open()
							});
					},
					on: {
						open: 'connected'
					}
				},
				connected: {
					entry: (context) => {
						Object.assign(
							context,
							buildContext(),
							{
								listRoom: () => this.m_Operator.listRoom(),
								joinRoom: (room, password) => this.m_Operator.joinRoom(room, password)
							}
						);
					},
					on: {
						join: 'inRoom'
					}
				},
				inRoom: {
					entry: (context) => {
						Object.assign(
							context,
							buildContext(),
							{
								send: (subject, message) => this.m_Operator.send(subject, message),
								canCommunication: () => true
							});
					}
				}
			}
		}).withContext(buildContext());

		this.m_ConnectionService = interpret(connectionMachine);
		this.m_ConnectionService.start();
	}

	ngOnDestroy(): void {
		this.m_ConnectionService.stop();
	}

	get receiver(): Observable<chat.IPacket> {
		return this.m_Operator.receiver;
	}

	getUserName(): Promise<string> {
		return this.m_LoginNameSubject.toPromise();
	}

	async open(): Promise<void> {
		let name = await this.m_ConnectionService.state.context.open();

		this.m_LoginNameSubject.next(name);
		this.m_LoginNameSubject.complete();
		this.m_ConnectionService.send('open');
	}

	listRoom(): Promise<chat.IRoom[]> {
		return this.m_ConnectionService.state.context.listRoom();
	}

	async joinRoom(room: string, password?: string): Promise<void> {
		await this.m_ConnectionService.state.context.joinRoom(room, password);
		this.m_ConnectionService.send('join');
	}

	send(subject: string, message: Uint8Array): void {
		return this.m_ConnectionService.state.context.send(subject, message);
	}

	canCommunication(): boolean {
		return this.m_ConnectionService.state.context.canCommunication();
	}
}
