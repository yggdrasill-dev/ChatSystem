import { RoomlistComponent } from './roomlist/roomlist.component';
import { RoomComponent } from './room/room.component';
import { NgModule } from '@angular/core';
import { Routes, RouterModule } from '@angular/router';

const routes: Routes = [
	{ path: '', component: RoomlistComponent },
	{ path: 'room', component: RoomComponent }
];

@NgModule({
	imports: [RouterModule.forRoot(routes)],
	exports: [RouterModule]
})
export class AppRoutingModule { }
