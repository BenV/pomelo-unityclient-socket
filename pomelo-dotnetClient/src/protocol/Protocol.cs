using System;
using System.Text;

using Pomelo.MiniJSON;

namespace Pomelo.DotNetClient
{
	public class Protocol
	{
		private MessageProtocol messageProtocol;
		private ProtocolState state;
		private Transporter transporter;
		private HandShakeService handshake;
		private HeartBeatService heartBeatService = null;
		private JsonObject handshakeUser = null;
		private Action<JsonObject> handshakeCallback = null;
		private PomeloClient pc;

		public PomeloClient getPomeloClient(){
			return this.pc;
		}

		public Protocol(PomeloClient pc, System.Net.Sockets.Socket socket){
			this.pc = pc;
			this.transporter = new Transporter (socket, this.processMessage);
			this.transporter.onDisconnect = onDisconnect;
			this.transporter.onConnect = onConnect;

			this.handshake = new HandShakeService(this);
			this.state = ProtocolState.start;
		}

		internal void start(JsonObject user, Action<JsonObject> callback){
			this.transporter.start();
			this.handshakeUser = user;
			this.handshakeCallback = callback;

			this.state = ProtocolState.handshaking;
		}

		internal void update(){
			this.transporter.update();
		}
		
		//Send notify, do not need id
		internal void send(string route, JsonObject msg){
			send (route, 0, msg);
		}

		//Send request, user request id 
		internal void send(string route, uint id, JsonObject msg){
			if(this.state != ProtocolState.working) return;

			byte[] body = messageProtocol.encode (route, id, msg); 

			send (PackageType.PKG_DATA, body);
		}

		internal void send(PackageType type){
			if(this.state == ProtocolState.closed) return;
			transporter.send(PackageProtocol.encode (type));
		}

		//Send system message, these message do not use messageProtocol
		internal void send(PackageType type, JsonObject msg){
			//This method only used to send system package
			if(type == PackageType .PKG_DATA) return;
			
			byte[] body = Encoding.UTF8.GetBytes(msg.ToString());

			send (type, body);
		}

		//Send message use the transporter
		internal void send(PackageType type, byte[] body){
			if(this.state == ProtocolState.closed) return;

			byte[] pkg = PackageProtocol.encode (type, body);

			transporter.send(pkg);
		}
	
		//Invoke by Transporter, process the message
		internal void processMessage(byte[] bytes){
			Package pkg = PackageProtocol.decode(bytes);
			
			//Ignore all the message except handshading at handshake stage
			if (pkg.type == PackageType.PKG_HANDSHAKE && this.state == ProtocolState.handshaking) {

				//Ignore all the message except handshading
				JsonObject data = Json.Deserialize(Encoding.UTF8.GetString(pkg.body)) as JsonObject;
				
				processHandshakeData(data);

				this.state = ProtocolState.working;

			}else if (pkg.type == PackageType.PKG_HEARTBEAT && this.state == ProtocolState.working){
				this.heartBeatService.resetTimeout();
			}else if (pkg.type == PackageType.PKG_DATA && this.state == ProtocolState.working) {
				this.heartBeatService.resetTimeout();
				pc.processMessage(messageProtocol.decode (pkg.body));
			}else if (pkg.type == PackageType.PKG_KICK) {
				this.close();
			}
		}
		
		private void processHandshakeData(JsonObject msg){
			//Handshake error
			if(!msg.ContainsKey("code") || !msg.ContainsKey("sys") || Convert.ToInt32(msg["code"]) != 200){
				throw new Exception("Handshake error! Please check your handshake config.");
			}

			//Set compress data
			JsonObject sys = (JsonObject)msg["sys"];

			JsonObject dict = new JsonObject();
			if(sys.ContainsKey("dict")) dict = (JsonObject)sys["dict"];

			JsonObject protos = new JsonObject();
			JsonObject serverProtos = new JsonObject();
			JsonObject clientProtos = new JsonObject();

			if(sys.ContainsKey("protos")){ 
				protos = (JsonObject)sys["protos"];
				serverProtos = (JsonObject)protos["server"];
				clientProtos = (JsonObject)protos["client"];
			}

			messageProtocol = new MessageProtocol (dict, serverProtos, clientProtos);

			//Init heartbeat service
			int interval = 0;
			if(sys.ContainsKey("heartbeat")) interval = Convert.ToInt32(sys["heartbeat"]);
			heartBeatService = new HeartBeatService(interval, this);

			if(interval > 0){
				heartBeatService.start();
			}

			//send ack and change protocol state
			handshake.ack();
			this.state = ProtocolState.working;

			//Invoke handshake callback
			JsonObject user = new JsonObject();
			if(msg.ContainsKey("user")) user = (JsonObject)msg["user"];
			handshake.invokeCallback(user);
		}

		private void onConnect() {
			Console.WriteLine("Protocol.onConnect, sending handshake");
			this.handshake.request(handshakeUser, handshakeCallback);
			this.handshakeUser = null;
			this.handshakeCallback = null;
		}

		//The socket disconnect
		private void onDisconnect(){
			this.pc.disconnect();
		}

		internal void close(){
			if(this.state == ProtocolState.closed){
				return;
			}

			this.state = ProtocolState.closed;
			transporter.close();

			if(heartBeatService != null) {
				heartBeatService.stop();
			}
		}
	}
}

