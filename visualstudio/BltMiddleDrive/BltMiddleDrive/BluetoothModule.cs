﻿using System;
using System.Collections;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace BltMiddleDrive {
    class BluetoothModule {
        ArrayList writers;
        String dbName = "Data Source=middle_drive.db";

        public async Task Init(Boolean isServer) {
            Console.WriteLine("starting Init");
            writers = new ArrayList();

            Console.WriteLine("Creating DB");
            CreateDB();

            Console.WriteLine("starting HttpServer");
            InitHttpServer();

            if (isServer) {
                Console.WriteLine("starting BluetoothServer");
                await InitBluetoothServer();
            } else {
                Console.WriteLine("starting BluetoothClient");
                await InitBluetoothClient();
            }

            while (true) { }
        }

        void CreateDB() {
            var con = new SQLiteConnection(dbName);
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS text(ID INTEGER PRIMARY KEY AUTOINCREMENT, datetime INT, line TEXT) ";
            cmd.ExecuteNonQuery();
            con.Close();
        }

        void InitHttpServer() {
            HttpListener httplistener = new HttpListener();
            httplistener.Prefixes.Add("http://localhost:8000/");
            httplistener.Start();
            httplistener.BeginGetContext(Requested, httplistener);
        }

        async void Requested(IAsyncResult result) {
            var listener = (HttpListener)result.AsyncState;
            var context = listener.EndGetContext(result);
            var res = context.Response;
            var req = context.Request;
            var param = new StreamReader(req.InputStream, Encoding.GetEncoding("utf-8")).ReadToEnd();
            if (writers.Count > 0) {
                await Send(param);
            }
            res.StatusCode = 200;
            res.Close();
        }


        async Task InitBluetoothServer() {
            Console.WriteLine(RfcommDeviceService.GetDeviceSelector(RfcommServiceId.ObexObjectPush));
            var _provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.ObexObjectPush);
            Console.WriteLine(RfcommServiceId.ObexObjectPush.AsString());
            StreamSocketListener listener = new StreamSocketListener();
            listener.ConnectionReceived += OnConnectionReceived;
            await listener.BindServiceNameAsync(_provider.ServiceId.AsString(), SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            _provider.StartAdvertising(listener);
        }


        //async Task InitBluetoothServer() {
        //    var devInfos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector());

        //    foreach (var devInfo in devInfos) {
        //        var bluetoothDevice = await BluetoothDevice.FromIdAsync(devInfo.Id);
        //        var rfcommDeviceServices = await bluetoothDevice.GetRfcommServicesAsync();
        //        if (rfcommDeviceServices.Services.Count != 0) {
        //            Console.WriteLine(devInfo.Name + devInfo.Id + "に接続しようとしています");
        //            var service = rfcommDeviceServices.Services[0];
        //            Console.WriteLine(service.ServiceId.AsString());
        //            var provider = await RfcommServiceProvider.CreateAsync(service.ServiceId);
        //            StreamSocketListener listener = new StreamSocketListener();
        //            listener.ConnectionReceived += OnConnectionReceived;
        //            await listener.BindServiceNameAsync(provider.ServiceId.AsString(), SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
        //            provider.StartAdvertising(listener);
        //            Console.WriteLine("running!");
        //            break;
        //        }
        //    }
        //}

        public async Task InitBluetoothClient() {
            Console.WriteLine(RfcommDeviceService.GetDeviceSelector(RfcommServiceId.ObexObjectPush));
            var informations = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector());

            foreach (var info in informations) {
                Console.WriteLine(info.Name);
            }

            if (informations.Count == 0) {
                Console.WriteLine("デバイスが見つかりません");
                return;
            }

            var bluetoothDevice = await BluetoothDevice.FromIdAsync(informations[0].Id);
            var rfcommServices = await bluetoothDevice.GetRfcommServicesAsync();

            if (rfcommServices.Services.Count == 0) {
                Console.WriteLine("サービスがみつかりません");
                return;
            }

            var _service = rfcommServices.Services[0];

            var services =
            await DeviceInformation.FindAllAsync(RfcommDeviceService.GetDeviceSelector(RfcommServiceId.ObexObjectPush));

            if (services.Count != 0) {
                _service = await RfcommDeviceService.FromIdAsync(services[0].Id);
            } else {
                Console.WriteLine("サービスが見つかりませんでした");
                return;
            }

            var _socket = new StreamSocket();

            await _socket.ConnectAsync(_service.ConnectionHostName, _service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            StartStream(_socket);
        }

        //async Task InitBluetoothClient() {
        //    var devInfos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector());

        //    foreach (var devInfo in devInfos) {
        //        var bluetoothDevice = await BluetoothDevice.FromIdAsync(devInfo.Id);
        //        var rfcommDeviceServices = await bluetoothDevice.GetRfcommServicesAsync();
        //        if (rfcommDeviceServices.Services.Count != 0) {
        //            Console.WriteLine(devInfo.Name + devInfo.Id + "に接続しようとしています");
        //            var service = rfcommDeviceServices.Services[0];
        //            Console.WriteLine(service.ServiceId.AsString());
        //            var socket = new StreamSocket();
        //            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
        //            StartStream(socket);
        //        }
        //    }
        //    Console.WriteLine("running!");
        //}

        void OnConnectionReceived(StreamSocketListener listener, StreamSocketListenerConnectionReceivedEventArgs args) {
            Console.WriteLine("connected");
            StartStream(args.Socket);
        }

        void StartStream(StreamSocket socket) {
            Console.WriteLine("接続しました");
            var writer = new DataWriter(socket.OutputStream);
            writer.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            writers.Add(writer);
            Send("hello");

            var reader = new DataReader(socket.InputStream);
            reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            Receive(reader);
        }

        async Task Send(String text) {
            var buff = Encoding.UTF8.GetBytes(text);
            foreach (DataWriter writer in writers) {
                writer.WriteInt32(buff.Length);
                writer.WriteString(text);
                await writer.StoreAsync();
            }
        }

        async void Receive(DataReader r) {
            while (true) {
                await r.LoadAsync(sizeof(int));
                var length = r.ReadInt32();
                await r.LoadAsync((uint)length);
                var text = r.ReadString((uint)length);
                Console.WriteLine("R:" + text);

                var con = new SQLiteConnection(dbName);
                con.Open();
                var cmd = con.CreateCommand();
                cmd.CommandText = "INSERT INTO text (datetime, line) VALUES (@p_datetime, @p_line)";
                var time = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                cmd.Parameters.Add(new SQLiteParameter("@p_datetime", time));
                cmd.Parameters.Add(new SQLiteParameter("@p_line", text));
                cmd.ExecuteNonQuery();
                con.Close();
            }
        }
    }
}
