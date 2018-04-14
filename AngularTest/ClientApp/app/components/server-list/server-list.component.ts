﻿import { Component, Inject, EventEmitter, Output } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Component({
    selector: 'server-list',
    templateUrl: './server-list.component.html'
})

export class ServerListComponent {

    public servers: Server[];
    public baseUrl: string;
    public httpClient: HttpClient;
    @Output() onServerChanged = new EventEmitter();

    constructor(httpClient: HttpClient, @Inject('BASE_URL') baseUrl: string) {
        this.baseUrl = baseUrl;
        this.httpClient = httpClient;
        this.servers = [];

        this.httpClient.get(baseUrl + 'api/Features/GetServers').subscribe(result => {
            this.servers = result as Server[];
            if (this.servers.length > 0) {
                this.onServerChanged.emit(this.servers[0]);
            }
        }, error => console.error(error));
    }

    serverChanged(server: any) {
        this.onServerChanged.emit(server);
    }
}

interface Server {
    name: string;
    credsRequiredToUpdate: boolean;
}