﻿using System;
using System.Net;
using System.Linq;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;

namespace Telnet
{
	public class TelnetServer : Telnet
	{
		TcpListener tcpListener;
		Thread listenderThread;
		Dictionary<Guid, ClientInfo> clients;
		
		//------------------------------------------------------------------------------------------
		
		public delegate bool ClientAuthMethod (string login, string password);
		public ClientAuthMethod ClientAuthProc;
		
		//------------------------------------------------------------------------------------------
		
		public delegate void ClientConnectEvent (Guid cg, TcpClient tcpClient);
		public event ClientConnectEvent onClientConnect;
		
		public delegate void ClientAuthEvent (TcpClient tcpClient);
		public event ClientAuthEvent onClientAuthSucceed;
		public event ClientAuthEvent onClientAuthFailed;
		
		public delegate void ClientTextReceived (Guid cg, string text);
		public event ClientTextReceived onClientRecStrLine;
		
		//------------------------------------------------------------------------------------------
		
		public TelnetServer ()
		{
			tcpListener = null;
			listenderThread = null;
			clients = new Dictionary<Guid, ClientInfo>();
			
			ClientAuthProc = null;
			onClientConnect += DefaultClientConnect;
			onClientAuthSucceed += DefaultClientAuthSucceed;
			onClientAuthFailed += DefaultClientAuthFailed;
			onClientRecStrLine += DefaultClientTextRec;
		}
		
		//------------------------------------------------------------------------------------------
		
		void DefaultClientConnect (Guid cg, TcpClient tcpClient) { }
		
		void DefaultClientAuthSucceed (TcpClient tcpClient) { Send(tcpClient, "OK"); }
		
		void DefaultClientAuthFailed (TcpClient tcpClient) { }
		
		void DefaultClientTextRec (Guid cg, string text) { }
		
		//------------------------------------------------------------------------------------------
		
		void ClientProc (object clientProcObj)
		{
			var cpi = (ClientProcInfo)clientProcObj;
			
			while (cpi.tcpClient.Connected)
			{
				var s = ReadNoEmpty(cpi.tcpClient.GetStream(), true);
				onClientRecStrLine(cpi.clientGuid, s);
			}
		}
		
		bool ClientAutorization (TcpClient tc)
		{
			bool result = true;
			
			if (ClientAuthProc != null)
			{
				WriteLine(tc.GetStream(), "login:");
				var cLogin = ReadNoEmpty(tc.GetStream(), true).Trim();
				
				WriteLine(tc.GetStream(), "passw:");
				var cPassw = ReadNoEmpty(tc.GetStream(), true).Trim();
				
				result = ClientAuthProc(cLogin, cPassw);
			}
			
			if (result)
				onClientAuthSucceed(tc);
			else
				onClientAuthFailed(tc);
			
			return result;
		}
		
		void ListenderProc ()
		{
			while (tcpListener != null)
			{
				TcpClient tc;
				
				try
				{
					tc = tcpListener.AcceptTcpClient();
				}
				catch
				{
					tc = null;
				}
				
				if (tc != null)
				{
					var cg = Guid.NewGuid();
					
					onClientConnect(cg, tc);
					
					if (tc != null)
						if (tc.Connected)
							if (ClientAutorization(tc))
							{
								var ct = new Thread(new ParameterizedThreadStart(ClientProc));
								ct.IsBackground = true;
								ct.Start(new ClientProcInfo(cg, tc));
								
								clients.Add(cg, new ClientInfo(tc, ct));
							}
				}
			}
		}
		
		public bool Start (string host, int port = 23)
		{
			tcpListener = new TcpListener(IPAddress.Parse(host), port);
			tcpListener.Start();
			
			listenderThread = new Thread(ListenderProc);
			listenderThread.IsBackground = true;
			listenderThread.Start();
				
			return true;
		}
		
		public void Stop ()
		{
			if (tcpListener != null)
				try { tcpListener.Stop(); } catch { }
			
			if (listenderThread != null)
				try { listenderThread.Abort(); } catch {  }
			
			DisconnectAll();
		}
		
		public void DisconnectAll ()
		{
			foreach (var clientGuid in clients.Keys.ToArray())
				Disconnect(clientGuid);
		}
		
		public void Disconnect (Guid clientGuid)
		{
			if (clients.ContainsKey(clientGuid))
			{
				var cinfo = clients[clientGuid];
				
				try { cinfo.clientThread.Abort(); } catch { }
				try { cinfo.tpcClient.Close(); } catch { }
				
				clients.Remove(clientGuid);
			}
		}
		
		//------------------------------------------------------------------------------------------
		
		public void Send (Guid gc, string text, string telnetPostfix = "\n\n>")
		{
			if (clients.ContainsKey(gc))
				Send(clients[gc].tpcClient, text, telnetPostfix);
		}
		
		public void Send (TcpClient tc, string text, string telnetPostfix = "\n\n>")
		{
			if (tc != null)
				if (tc.Connected)
					Write(tc.GetStream(), text + (string.IsNullOrEmpty(telnetPostfix) ? "" : telnetPostfix));
		}
		
		public void SendLine (Guid gc, string strLine)
		{
			if (clients.ContainsKey(gc))
				WriteLine(clients[gc].tpcClient.GetStream(), strLine);
		}
	}
}