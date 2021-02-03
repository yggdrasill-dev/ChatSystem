import { ChatClientService } from './../services/chat-client.service';
import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';

@Component({
	selector: 'app-roomlist',
	templateUrl: './roomlist.component.html',
	styleUrls: ['./roomlist.component.scss']
})
export class RoomlistComponent implements OnInit {
	rooms: string[] = [];
	selectedRoom: string = '';
	selectedType: string = 'join';
	joinData: { name: string; password: string } = { name: '', password: '' };
	createData: { name: string; password: string } = { name: '', password: '' };

	constructor(
		private m_ChatClient: ChatClientService,
		private m_Router: Router
	) { }

	async ngOnInit(): Promise<void> {
		await this.m_ChatClient.open();

		this.rooms = await this.m_ChatClient.listRoom();
	}

	async createRoom(): Promise<void> {
		this.m_ChatClient.joinRoom(this.createData.name);
		await this.m_Router.navigate(['/room']);
	}

	async joinRoom(): Promise<void>{
		await this.m_ChatClient.joinRoom(this.joinData.name);
		await this.m_Router.navigate(['/room']);
	}
}
