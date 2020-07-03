using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace VolumeControl
{
    public class Server
    {
        private readonly ClientListener m_clientListener;
        private readonly TcpListener m_tcpListener;
        private readonly List<TcpClient> m_clients = new List<TcpClient>();
        private bool m_running;
        private readonly ASCIIEncoding m_encoder = new ASCIIEncoding();

        public Server(ClientListener clientListener)
        {
            m_tcpListener = new TcpListener(IPAddress.Any, 3000);
            var listenThread = new Thread(ListenForClients);
            listenThread.Start();
            m_clientListener = clientListener;
        }

        public bool isRunning()
        {
            return m_running;
        }

        public void stop()
        {
            m_running = false;

            m_tcpListener.Stop();

            lock ( this )
            {
                foreach (var client in m_clients)
                {
                    //Console.WriteLine("Closing client connection...");

                    try
                    {
                        client.Close();
                    }
                    catch (IOException)
                    { }
                    catch (ObjectDisposedException)
                    { }
                }
            }
        }

        private void ListenForClients()
        {
            m_tcpListener.Start();

            m_running = true;
            m_clientListener.onServerStart();

            while (m_running)
            {
                //blocks until a client has connected to the server
                try
                {
                    var client = m_tcpListener.AcceptTcpClient();
                    //Console.WriteLine("connection accepted");
                    //create a thread to handle communication 
                    //with connected client
                    var clientThread = new Thread(HandleClientComm);
                    clientThread.Start(client);
                }
                catch(SocketException)
                {

                }
            }

            m_running = false;
            m_clientListener.onServerEnd();
        }

        private void HandleClientComm(object client)
        {
            //Console.WriteLine("Client connected");

            var tcpClient = (TcpClient)client;
            lock (this)
            {
                m_clients.Add(tcpClient);
            }

            m_clientListener.onClientConnect();

            try
            {
                var clientStream = tcpClient.GetStream();
                var bufferedStream = new BufferedStream(clientStream);
                var streamReader = new StreamReader(bufferedStream);

                while (tcpClient.Connected)
                {
                    string message;
                    try
                    {
                        //blocks until a client sends a message
                        //bytesRead = clientStream.Read(message, 0, 4096);
                        message = streamReader.ReadLine();
                    }
                    catch
                    {
                        //a socket error has occured
                        break;
                    }

                    // End of message
                    if (message != null)
                    {
                        //Console.WriteLine("Message received");
                        m_clientListener?.onClientMessage(message, tcpClient);
                        //else
                        //{
                            //Console.WriteLine("Message missed, no listener");
                        //}
                    }
                    else
                    {
                        //Console.WriteLine("No message from client, close socket.");
                        break;
                    }
                }
            }
            catch(InvalidOperationException)
            {

            }
            finally
            {
                lock (this)
                {
                    m_clients.Remove(tcpClient);
                }
                //tcpClient.Close();
                tcpClient.Dispose();
                //Console.WriteLine("Client disconnected");
            }
        }

        public void sendData(string data)
        {
            var finalData = data;
            if(!string.IsNullOrEmpty(data) && data[^1] != '\n')
            {
                finalData += '\n';
            }

            List<TcpClient> clients;
            lock (this)
            {
                clients = m_clients.ToList();
            }

            //System.Diagnostics.Debug.WriteLine(finalData);
            var buffer = m_encoder.GetBytes(finalData);

            foreach (var client in clients)
            {
                //Console.WriteLine("Sending data to a client...");

                try
                {
                    var clientStream = client.GetStream();

                    clientStream.Write(buffer, 0, buffer.Length);
                    clientStream.Flush();
                }
                catch(IOException)
                { }
                catch (ObjectDisposedException)
                { }
            }
        }
    }

    public interface ClientListener
    {
        void onClientMessage( string message, TcpClient tcpClient);
        void onClientConnect();

        void onServerStart();
        void onServerEnd();
    }
}
