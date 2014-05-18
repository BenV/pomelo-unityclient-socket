using System;
using System.Net.Sockets;
using UnityEngine;

namespace Pomelo.DotNetClient {
	class StateObject {
		public const int BufferSize = 1024;
		internal byte[] buffer = new byte[BufferSize];
	}
	
	public class Transporter {
		public const int WSAEWOULDBLOCK = 10035;
		public const int HeadLength = 4;
		
		private Socket socket;
		private Action<byte[]> messageProcesser;
		
		//Used for get message
		private TransportState transportState;
		private StateObject state = new StateObject();
		private byte[] headBuffer = new byte[4];
		private byte[] buffer;
		private int bufferOffset = 0;
		private int pkgLength = 0;
		internal Action onDisconnect = null;
		internal Action onConnect = null;
		
		public Transporter(Socket socket, Action<byte[]> processer) {
			this.socket = socket;
			this.messageProcesser = processer;
			transportState = TransportState.connect;
		}
		
		public void start() {

		}
		
		public void send(byte[] b) {
			if(this.transportState != TransportState.closed) { 
				this.socket.Send(b);
			}
		}
		
		public void receive() {
			try {
				int length = socket.Receive(state.buffer);
				if(length > 0) {
					processBytes(state.buffer, 0, length);
				} else {
					close();
				}
			} catch(SocketException e) {
				if(e.ErrorCode != WSAEWOULDBLOCK) {
					close();
				}
			}
		}

		public void update() {
			if(this.transportState == TransportState.connect) {
				try {
					if(this.socket.Poll(-1, SelectMode.SelectWrite)) {
						this.transportState = TransportState.readHead;
						if(this.onConnect != null)
							this.onConnect();
					}
				} catch(SocketException e) {
					if(e.ErrorCode == WSAEWOULDBLOCK) {
						Debug.Log("Waiting for Socket to finish connecting...");
					} else {
						Debug.Log("SocketException, code: " + e.ErrorCode);
						close();
					}
				}
			} else if(this.transportState != TransportState.closed) {
				this.receive();
			}
		}
		
		internal void close() {
			if(this.transportState != TransportState.closed) {
				this.transportState = TransportState.closed;
				if(this.onDisconnect != null) {
					this.onDisconnect();
				}
			}
		}
		
		internal void processBytes(byte[] bytes, int offset, int limit) {
			if(this.transportState == TransportState.readHead) {
				readHead(bytes, offset, limit);
			} else if(this.transportState == TransportState.readBody) {
				readBody(bytes, offset, limit);
			}
		}
		
		private bool readHead(byte[] bytes, int offset, int limit) {
			int length = limit - offset;
			int headNum = HeadLength - bufferOffset;
			
			if(length >= headNum) {
				//Write head buffer
				writeBytes(bytes, offset, headNum, bufferOffset, headBuffer);
				//Get package length
				pkgLength = (headBuffer[1] << 16) + (headBuffer[2] << 8) + headBuffer[3];
				
				//Init message buffer
				buffer = new byte[HeadLength + pkgLength];
				writeBytes(headBuffer, 0, HeadLength, buffer);
				offset += headNum;
				bufferOffset = HeadLength;
				this.transportState = TransportState.readBody;
				
				if(offset <= limit)
					processBytes(bytes, offset, limit);
				return true;
			} else {
				writeBytes(bytes, offset, length, bufferOffset, headBuffer);
				bufferOffset += length;
				return false;
			}
		}
		
		private void readBody(byte[] bytes, int offset, int limit) {
			int length = pkgLength + HeadLength - bufferOffset;
			if((offset + length) <= limit) {
				writeBytes(bytes, offset, length, bufferOffset, buffer);
				offset += length;
				
				//Invoke the protocol api to handle the message
				this.messageProcesser.Invoke(buffer);
				this.bufferOffset = 0;
				this.pkgLength = 0;
				
				if(this.transportState != TransportState.closed)
					this.transportState = TransportState.readHead;
				if(offset < limit)
					processBytes(bytes, offset, limit); 
			} else {
				writeBytes(bytes, offset, limit - offset, bufferOffset, buffer);
				bufferOffset += limit - offset;
				this.transportState = TransportState.readBody;
			}			
		}
		
		private void writeBytes(byte[] source, int s, int length, byte[] target) {
			writeBytes(source, s, length, 0, target);
		}
		
		private void writeBytes(byte[] source, int s, int length, int offset, byte[] target) {
			for(int i = 0; i < length; i++) {
				target[offset + i] = source[s + i];
			}
		}
		
		private void print(byte[] bytes, int offset, int length) {
			for(int i = offset; i < length; i++)
				Console.Write(Convert.ToString(bytes[i], 16) + " ");
			Console.WriteLine();
		}
	}
}