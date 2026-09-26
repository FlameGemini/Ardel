"use strict";
(() => {
  var __create = Object.create;
  var __defProp = Object.defineProperty;
  var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
  var __getOwnPropNames = Object.getOwnPropertyNames;
  var __getProtoOf = Object.getPrototypeOf;
  var __hasOwnProp = Object.prototype.hasOwnProperty;
  var __commonJS = (cb, mod) => function __require() {
    return mod || (0, cb[__getOwnPropNames(cb)[0]])((mod = { exports: {} }).exports, mod), mod.exports;
  };
  var __copyProps = (to, from, except, desc) => {
    if (from && typeof from === "object" || typeof from === "function") {
      for (let key of __getOwnPropNames(from))
        if (!__hasOwnProp.call(to, key) && key !== except)
          __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
    }
    return to;
  };
  var __toESM = (mod, isNodeMode, target) => (target = mod != null ? __create(__getProtoOf(mod)) : {}, __copyProps(
    // If the importer is in node compatibility mode or this is not an ESM
    // file that has been converted to a CommonJS file using a Babel-
    // compatible transform (i.e. "__esModule" has not been set), then set
    // "default" to the CommonJS "module.exports" for node compatibility.
    isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target,
    mod
  ));

  // node_modules/paho-mqtt/paho-mqtt.js
  var require_paho_mqtt = __commonJS({
    "node_modules/paho-mqtt/paho-mqtt.js"(exports, module) {
      (function ExportLibrary(root, factory) {
        if (typeof exports === "object" && typeof module === "object") {
          module.exports = factory();
        } else if (typeof define === "function" && define.amd) {
          define(factory);
        } else if (typeof exports === "object") {
          exports = factory();
        } else {
          root.Paho = factory();
        }
      })(exports, function LibraryFactory() {
        var PahoMQTT = (function(global2) {
          var version = "@VERSION@-@BUILDLEVEL@";
          var localStorage = global2.localStorage || /* @__PURE__ */ (function() {
            var data = {};
            return {
              setItem: function(key, item) {
                data[key] = item;
              },
              getItem: function(key) {
                return data[key];
              },
              removeItem: function(key) {
                delete data[key];
              }
            };
          })();
          var MESSAGE_TYPE = {
            CONNECT: 1,
            CONNACK: 2,
            PUBLISH: 3,
            PUBACK: 4,
            PUBREC: 5,
            PUBREL: 6,
            PUBCOMP: 7,
            SUBSCRIBE: 8,
            SUBACK: 9,
            UNSUBSCRIBE: 10,
            UNSUBACK: 11,
            PINGREQ: 12,
            PINGRESP: 13,
            DISCONNECT: 14
          };
          var validate = function(obj, keys) {
            for (var key in obj) {
              if (obj.hasOwnProperty(key)) {
                if (keys.hasOwnProperty(key)) {
                  if (typeof obj[key] !== keys[key])
                    throw new Error(format(ERROR.INVALID_TYPE, [typeof obj[key], key]));
                } else {
                  var errorStr = "Unknown property, " + key + ". Valid properties are:";
                  for (var validKey in keys)
                    if (keys.hasOwnProperty(validKey))
                      errorStr = errorStr + " " + validKey;
                  throw new Error(errorStr);
                }
              }
            }
          };
          var scope = function(f, scope2) {
            return function() {
              return f.apply(scope2, arguments);
            };
          };
          var ERROR = {
            OK: { code: 0, text: "AMQJSC0000I OK." },
            CONNECT_TIMEOUT: { code: 1, text: "AMQJSC0001E Connect timed out." },
            SUBSCRIBE_TIMEOUT: { code: 2, text: "AMQJS0002E Subscribe timed out." },
            UNSUBSCRIBE_TIMEOUT: { code: 3, text: "AMQJS0003E Unsubscribe timed out." },
            PING_TIMEOUT: { code: 4, text: "AMQJS0004E Ping timed out." },
            INTERNAL_ERROR: { code: 5, text: "AMQJS0005E Internal error. Error Message: {0}, Stack trace: {1}" },
            CONNACK_RETURNCODE: { code: 6, text: "AMQJS0006E Bad Connack return code:{0} {1}." },
            SOCKET_ERROR: { code: 7, text: "AMQJS0007E Socket error:{0}." },
            SOCKET_CLOSE: { code: 8, text: "AMQJS0008I Socket closed." },
            MALFORMED_UTF: { code: 9, text: "AMQJS0009E Malformed UTF data:{0} {1} {2}." },
            UNSUPPORTED: { code: 10, text: "AMQJS0010E {0} is not supported by this browser." },
            INVALID_STATE: { code: 11, text: "AMQJS0011E Invalid state {0}." },
            INVALID_TYPE: { code: 12, text: "AMQJS0012E Invalid type {0} for {1}." },
            INVALID_ARGUMENT: { code: 13, text: "AMQJS0013E Invalid argument {0} for {1}." },
            UNSUPPORTED_OPERATION: { code: 14, text: "AMQJS0014E Unsupported operation." },
            INVALID_STORED_DATA: { code: 15, text: "AMQJS0015E Invalid data in local storage key={0} value={1}." },
            INVALID_MQTT_MESSAGE_TYPE: { code: 16, text: "AMQJS0016E Invalid MQTT message type {0}." },
            MALFORMED_UNICODE: { code: 17, text: "AMQJS0017E Malformed Unicode string:{0} {1}." },
            BUFFER_FULL: { code: 18, text: "AMQJS0018E Message buffer is full, maximum buffer size: {0}." }
          };
          var CONNACK_RC = {
            0: "Connection Accepted",
            1: "Connection Refused: unacceptable protocol version",
            2: "Connection Refused: identifier rejected",
            3: "Connection Refused: server unavailable",
            4: "Connection Refused: bad user name or password",
            5: "Connection Refused: not authorized"
          };
          var format = function(error, substitutions) {
            var text = error.text;
            if (substitutions) {
              var field, start;
              for (var i = 0; i < substitutions.length; i++) {
                field = "{" + i + "}";
                start = text.indexOf(field);
                if (start > 0) {
                  var part1 = text.substring(0, start);
                  var part2 = text.substring(start + field.length);
                  text = part1 + substitutions[i] + part2;
                }
              }
            }
            return text;
          };
          var MqttProtoIdentifierv3 = [0, 6, 77, 81, 73, 115, 100, 112, 3];
          var MqttProtoIdentifierv4 = [0, 4, 77, 81, 84, 84, 4];
          var WireMessage = function(type, options) {
            this.type = type;
            for (var name in options) {
              if (options.hasOwnProperty(name)) {
                this[name] = options[name];
              }
            }
          };
          WireMessage.prototype.encode = function() {
            var first = (this.type & 15) << 4;
            var remLength = 0;
            var topicStrLength = [];
            var destinationNameLength = 0;
            var willMessagePayloadBytes;
            if (this.messageIdentifier !== void 0)
              remLength += 2;
            switch (this.type) {
              // If this a Connect then we need to include 12 bytes for its header
              case MESSAGE_TYPE.CONNECT:
                switch (this.mqttVersion) {
                  case 3:
                    remLength += MqttProtoIdentifierv3.length + 3;
                    break;
                  case 4:
                    remLength += MqttProtoIdentifierv4.length + 3;
                    break;
                }
                remLength += UTF8Length(this.clientId) + 2;
                if (this.willMessage !== void 0) {
                  remLength += UTF8Length(this.willMessage.destinationName) + 2;
                  willMessagePayloadBytes = this.willMessage.payloadBytes;
                  if (!(willMessagePayloadBytes instanceof Uint8Array))
                    willMessagePayloadBytes = new Uint8Array(payloadBytes);
                  remLength += willMessagePayloadBytes.byteLength + 2;
                }
                if (this.userName !== void 0)
                  remLength += UTF8Length(this.userName) + 2;
                if (this.password !== void 0)
                  remLength += UTF8Length(this.password) + 2;
                break;
              // Subscribe, Unsubscribe can both contain topic strings
              case MESSAGE_TYPE.SUBSCRIBE:
                first |= 2;
                for (var i = 0; i < this.topics.length; i++) {
                  topicStrLength[i] = UTF8Length(this.topics[i]);
                  remLength += topicStrLength[i] + 2;
                }
                remLength += this.requestedQos.length;
                break;
              case MESSAGE_TYPE.UNSUBSCRIBE:
                first |= 2;
                for (var i = 0; i < this.topics.length; i++) {
                  topicStrLength[i] = UTF8Length(this.topics[i]);
                  remLength += topicStrLength[i] + 2;
                }
                break;
              case MESSAGE_TYPE.PUBREL:
                first |= 2;
                break;
              case MESSAGE_TYPE.PUBLISH:
                if (this.payloadMessage.duplicate) first |= 8;
                first = first |= this.payloadMessage.qos << 1;
                if (this.payloadMessage.retained) first |= 1;
                destinationNameLength = UTF8Length(this.payloadMessage.destinationName);
                remLength += destinationNameLength + 2;
                var payloadBytes = this.payloadMessage.payloadBytes;
                remLength += payloadBytes.byteLength;
                if (payloadBytes instanceof ArrayBuffer)
                  payloadBytes = new Uint8Array(payloadBytes);
                else if (!(payloadBytes instanceof Uint8Array))
                  payloadBytes = new Uint8Array(payloadBytes.buffer);
                break;
              case MESSAGE_TYPE.DISCONNECT:
                break;
              default:
                break;
            }
            var mbi = encodeMBI(remLength);
            var pos = mbi.length + 1;
            var buffer = new ArrayBuffer(remLength + pos);
            var byteStream = new Uint8Array(buffer);
            byteStream[0] = first;
            byteStream.set(mbi, 1);
            if (this.type == MESSAGE_TYPE.PUBLISH)
              pos = writeString(this.payloadMessage.destinationName, destinationNameLength, byteStream, pos);
            else if (this.type == MESSAGE_TYPE.CONNECT) {
              switch (this.mqttVersion) {
                case 3:
                  byteStream.set(MqttProtoIdentifierv3, pos);
                  pos += MqttProtoIdentifierv3.length;
                  break;
                case 4:
                  byteStream.set(MqttProtoIdentifierv4, pos);
                  pos += MqttProtoIdentifierv4.length;
                  break;
              }
              var connectFlags = 0;
              if (this.cleanSession)
                connectFlags = 2;
              if (this.willMessage !== void 0) {
                connectFlags |= 4;
                connectFlags |= this.willMessage.qos << 3;
                if (this.willMessage.retained) {
                  connectFlags |= 32;
                }
              }
              if (this.userName !== void 0)
                connectFlags |= 128;
              if (this.password !== void 0)
                connectFlags |= 64;
              byteStream[pos++] = connectFlags;
              pos = writeUint16(this.keepAliveInterval, byteStream, pos);
            }
            if (this.messageIdentifier !== void 0)
              pos = writeUint16(this.messageIdentifier, byteStream, pos);
            switch (this.type) {
              case MESSAGE_TYPE.CONNECT:
                pos = writeString(this.clientId, UTF8Length(this.clientId), byteStream, pos);
                if (this.willMessage !== void 0) {
                  pos = writeString(this.willMessage.destinationName, UTF8Length(this.willMessage.destinationName), byteStream, pos);
                  pos = writeUint16(willMessagePayloadBytes.byteLength, byteStream, pos);
                  byteStream.set(willMessagePayloadBytes, pos);
                  pos += willMessagePayloadBytes.byteLength;
                }
                if (this.userName !== void 0)
                  pos = writeString(this.userName, UTF8Length(this.userName), byteStream, pos);
                if (this.password !== void 0)
                  pos = writeString(this.password, UTF8Length(this.password), byteStream, pos);
                break;
              case MESSAGE_TYPE.PUBLISH:
                byteStream.set(payloadBytes, pos);
                break;
              //    	    case MESSAGE_TYPE.PUBREC:
              //    	    case MESSAGE_TYPE.PUBREL:
              //    	    case MESSAGE_TYPE.PUBCOMP:
              //    	    	break;
              case MESSAGE_TYPE.SUBSCRIBE:
                for (var i = 0; i < this.topics.length; i++) {
                  pos = writeString(this.topics[i], topicStrLength[i], byteStream, pos);
                  byteStream[pos++] = this.requestedQos[i];
                }
                break;
              case MESSAGE_TYPE.UNSUBSCRIBE:
                for (var i = 0; i < this.topics.length; i++)
                  pos = writeString(this.topics[i], topicStrLength[i], byteStream, pos);
                break;
              default:
            }
            return buffer;
          };
          function decodeMessage(input, pos) {
            var startingPos = pos;
            var first = input[pos];
            var type = first >> 4;
            var messageInfo = first &= 15;
            pos += 1;
            var digit;
            var remLength = 0;
            var multiplier = 1;
            do {
              if (pos == input.length) {
                return [null, startingPos];
              }
              digit = input[pos++];
              remLength += (digit & 127) * multiplier;
              multiplier *= 128;
            } while ((digit & 128) !== 0);
            var endPos = pos + remLength;
            if (endPos > input.length) {
              return [null, startingPos];
            }
            var wireMessage = new WireMessage(type);
            switch (type) {
              case MESSAGE_TYPE.CONNACK:
                var connectAcknowledgeFlags = input[pos++];
                if (connectAcknowledgeFlags & 1)
                  wireMessage.sessionPresent = true;
                wireMessage.returnCode = input[pos++];
                break;
              case MESSAGE_TYPE.PUBLISH:
                var qos = messageInfo >> 1 & 3;
                var len = readUint16(input, pos);
                pos += 2;
                var topicName = parseUTF8(input, pos, len);
                pos += len;
                if (qos > 0) {
                  wireMessage.messageIdentifier = readUint16(input, pos);
                  pos += 2;
                }
                var message = new Message(input.subarray(pos, endPos));
                if ((messageInfo & 1) == 1)
                  message.retained = true;
                if ((messageInfo & 8) == 8)
                  message.duplicate = true;
                message.qos = qos;
                message.destinationName = topicName;
                wireMessage.payloadMessage = message;
                break;
              case MESSAGE_TYPE.PUBACK:
              case MESSAGE_TYPE.PUBREC:
              case MESSAGE_TYPE.PUBREL:
              case MESSAGE_TYPE.PUBCOMP:
              case MESSAGE_TYPE.UNSUBACK:
                wireMessage.messageIdentifier = readUint16(input, pos);
                break;
              case MESSAGE_TYPE.SUBACK:
                wireMessage.messageIdentifier = readUint16(input, pos);
                pos += 2;
                wireMessage.returnCode = input.subarray(pos, endPos);
                break;
              default:
                break;
            }
            return [wireMessage, endPos];
          }
          function writeUint16(input, buffer, offset) {
            buffer[offset++] = input >> 8;
            buffer[offset++] = input % 256;
            return offset;
          }
          function writeString(input, utf8Length, buffer, offset) {
            offset = writeUint16(utf8Length, buffer, offset);
            stringToUTF8(input, buffer, offset);
            return offset + utf8Length;
          }
          function readUint16(buffer, offset) {
            return 256 * buffer[offset] + buffer[offset + 1];
          }
          function encodeMBI(number) {
            var output = new Array(1);
            var numBytes = 0;
            do {
              var digit = number % 128;
              number = number >> 7;
              if (number > 0) {
                digit |= 128;
              }
              output[numBytes++] = digit;
            } while (number > 0 && numBytes < 4);
            return output;
          }
          function UTF8Length(input) {
            var output = 0;
            for (var i = 0; i < input.length; i++) {
              var charCode = input.charCodeAt(i);
              if (charCode > 2047) {
                if (55296 <= charCode && charCode <= 56319) {
                  i++;
                  output++;
                }
                output += 3;
              } else if (charCode > 127)
                output += 2;
              else
                output++;
            }
            return output;
          }
          function stringToUTF8(input, output, start) {
            var pos = start;
            for (var i = 0; i < input.length; i++) {
              var charCode = input.charCodeAt(i);
              if (55296 <= charCode && charCode <= 56319) {
                var lowCharCode = input.charCodeAt(++i);
                if (isNaN(lowCharCode)) {
                  throw new Error(format(ERROR.MALFORMED_UNICODE, [charCode, lowCharCode]));
                }
                charCode = (charCode - 55296 << 10) + (lowCharCode - 56320) + 65536;
              }
              if (charCode <= 127) {
                output[pos++] = charCode;
              } else if (charCode <= 2047) {
                output[pos++] = charCode >> 6 & 31 | 192;
                output[pos++] = charCode & 63 | 128;
              } else if (charCode <= 65535) {
                output[pos++] = charCode >> 12 & 15 | 224;
                output[pos++] = charCode >> 6 & 63 | 128;
                output[pos++] = charCode & 63 | 128;
              } else {
                output[pos++] = charCode >> 18 & 7 | 240;
                output[pos++] = charCode >> 12 & 63 | 128;
                output[pos++] = charCode >> 6 & 63 | 128;
                output[pos++] = charCode & 63 | 128;
              }
            }
            return output;
          }
          function parseUTF8(input, offset, length) {
            var output = "";
            var utf16;
            var pos = offset;
            while (pos < offset + length) {
              var byte1 = input[pos++];
              if (byte1 < 128)
                utf16 = byte1;
              else {
                var byte2 = input[pos++] - 128;
                if (byte2 < 0)
                  throw new Error(format(ERROR.MALFORMED_UTF, [byte1.toString(16), byte2.toString(16), ""]));
                if (byte1 < 224)
                  utf16 = 64 * (byte1 - 192) + byte2;
                else {
                  var byte3 = input[pos++] - 128;
                  if (byte3 < 0)
                    throw new Error(format(ERROR.MALFORMED_UTF, [byte1.toString(16), byte2.toString(16), byte3.toString(16)]));
                  if (byte1 < 240)
                    utf16 = 4096 * (byte1 - 224) + 64 * byte2 + byte3;
                  else {
                    var byte4 = input[pos++] - 128;
                    if (byte4 < 0)
                      throw new Error(format(ERROR.MALFORMED_UTF, [byte1.toString(16), byte2.toString(16), byte3.toString(16), byte4.toString(16)]));
                    if (byte1 < 248)
                      utf16 = 262144 * (byte1 - 240) + 4096 * byte2 + 64 * byte3 + byte4;
                    else
                      throw new Error(format(ERROR.MALFORMED_UTF, [byte1.toString(16), byte2.toString(16), byte3.toString(16), byte4.toString(16)]));
                  }
                }
              }
              if (utf16 > 65535) {
                utf16 -= 65536;
                output += String.fromCharCode(55296 + (utf16 >> 10));
                utf16 = 56320 + (utf16 & 1023);
              }
              output += String.fromCharCode(utf16);
            }
            return output;
          }
          var Pinger = function(client, keepAliveInterval) {
            this._client = client;
            this._keepAliveInterval = keepAliveInterval * 1e3;
            this.isReset = false;
            var pingReq = new WireMessage(MESSAGE_TYPE.PINGREQ).encode();
            var doTimeout = function(pinger) {
              return function() {
                return doPing.apply(pinger);
              };
            };
            var doPing = function() {
              if (!this.isReset) {
                this._client._trace("Pinger.doPing", "Timed out");
                this._client._disconnected(ERROR.PING_TIMEOUT.code, format(ERROR.PING_TIMEOUT));
              } else {
                this.isReset = false;
                this._client._trace("Pinger.doPing", "send PINGREQ");
                this._client.socket.send(pingReq);
                this.timeout = setTimeout(doTimeout(this), this._keepAliveInterval);
              }
            };
            this.reset = function() {
              this.isReset = true;
              clearTimeout(this.timeout);
              if (this._keepAliveInterval > 0)
                this.timeout = setTimeout(doTimeout(this), this._keepAliveInterval);
            };
            this.cancel = function() {
              clearTimeout(this.timeout);
            };
          };
          var Timeout = function(client, timeoutSeconds, action, args) {
            if (!timeoutSeconds)
              timeoutSeconds = 30;
            var doTimeout = function(action2, client2, args2) {
              return function() {
                return action2.apply(client2, args2);
              };
            };
            this.timeout = setTimeout(doTimeout(action, client, args), timeoutSeconds * 1e3);
            this.cancel = function() {
              clearTimeout(this.timeout);
            };
          };
          var ClientImpl = function(uri, host, port, path, clientId) {
            if (!("WebSocket" in global2 && global2.WebSocket !== null)) {
              throw new Error(format(ERROR.UNSUPPORTED, ["WebSocket"]));
            }
            if (!("ArrayBuffer" in global2 && global2.ArrayBuffer !== null)) {
              throw new Error(format(ERROR.UNSUPPORTED, ["ArrayBuffer"]));
            }
            this._trace("Paho.Client", uri, host, port, path, clientId);
            this.host = host;
            this.port = port;
            this.path = path;
            this.uri = uri;
            this.clientId = clientId;
            this._wsuri = null;
            this._localKey = host + ":" + port + (path != "/mqtt" ? ":" + path : "") + ":" + clientId + ":";
            this._msg_queue = [];
            this._buffered_msg_queue = [];
            this._sentMessages = {};
            this._receivedMessages = {};
            this._notify_msg_sent = {};
            this._message_identifier = 1;
            this._sequence = 0;
            for (var key in localStorage)
              if (key.indexOf("Sent:" + this._localKey) === 0 || key.indexOf("Received:" + this._localKey) === 0)
                this.restore(key);
          };
          ClientImpl.prototype.host = null;
          ClientImpl.prototype.port = null;
          ClientImpl.prototype.path = null;
          ClientImpl.prototype.uri = null;
          ClientImpl.prototype.clientId = null;
          ClientImpl.prototype.socket = null;
          ClientImpl.prototype.connected = false;
          ClientImpl.prototype.maxMessageIdentifier = 65536;
          ClientImpl.prototype.connectOptions = null;
          ClientImpl.prototype.hostIndex = null;
          ClientImpl.prototype.onConnected = null;
          ClientImpl.prototype.onConnectionLost = null;
          ClientImpl.prototype.onMessageDelivered = null;
          ClientImpl.prototype.onMessageArrived = null;
          ClientImpl.prototype.traceFunction = null;
          ClientImpl.prototype._msg_queue = null;
          ClientImpl.prototype._buffered_msg_queue = null;
          ClientImpl.prototype._connectTimeout = null;
          ClientImpl.prototype.sendPinger = null;
          ClientImpl.prototype.receivePinger = null;
          ClientImpl.prototype._reconnectInterval = 1;
          ClientImpl.prototype._reconnecting = false;
          ClientImpl.prototype._reconnectTimeout = null;
          ClientImpl.prototype.disconnectedPublishing = false;
          ClientImpl.prototype.disconnectedBufferSize = 5e3;
          ClientImpl.prototype.receiveBuffer = null;
          ClientImpl.prototype._traceBuffer = null;
          ClientImpl.prototype._MAX_TRACE_ENTRIES = 100;
          ClientImpl.prototype.connect = function(connectOptions) {
            var connectOptionsMasked = this._traceMask(connectOptions, "password");
            this._trace("Client.connect", connectOptionsMasked, this.socket, this.connected);
            if (this.connected)
              throw new Error(format(ERROR.INVALID_STATE, ["already connected"]));
            if (this.socket)
              throw new Error(format(ERROR.INVALID_STATE, ["already connected"]));
            if (this._reconnecting) {
              this._reconnectTimeout.cancel();
              this._reconnectTimeout = null;
              this._reconnecting = false;
            }
            this.connectOptions = connectOptions;
            this._reconnectInterval = 1;
            this._reconnecting = false;
            if (connectOptions.uris) {
              this.hostIndex = 0;
              this._doConnect(connectOptions.uris[0]);
            } else {
              this._doConnect(this.uri);
            }
          };
          ClientImpl.prototype.subscribe = function(filter, subscribeOptions) {
            this._trace("Client.subscribe", filter, subscribeOptions);
            if (!this.connected)
              throw new Error(format(ERROR.INVALID_STATE, ["not connected"]));
            var wireMessage = new WireMessage(MESSAGE_TYPE.SUBSCRIBE);
            wireMessage.topics = filter.constructor === Array ? filter : [filter];
            if (subscribeOptions.qos === void 0)
              subscribeOptions.qos = 0;
            wireMessage.requestedQos = [];
            for (var i = 0; i < wireMessage.topics.length; i++)
              wireMessage.requestedQos[i] = subscribeOptions.qos;
            if (subscribeOptions.onSuccess) {
              wireMessage.onSuccess = function(grantedQos) {
                subscribeOptions.onSuccess({ invocationContext: subscribeOptions.invocationContext, grantedQos });
              };
            }
            if (subscribeOptions.onFailure) {
              wireMessage.onFailure = function(errorCode) {
                subscribeOptions.onFailure({ invocationContext: subscribeOptions.invocationContext, errorCode, errorMessage: format(errorCode) });
              };
            }
            if (subscribeOptions.timeout) {
              wireMessage.timeOut = new Timeout(
                this,
                subscribeOptions.timeout,
                subscribeOptions.onFailure,
                [{
                  invocationContext: subscribeOptions.invocationContext,
                  errorCode: ERROR.SUBSCRIBE_TIMEOUT.code,
                  errorMessage: format(ERROR.SUBSCRIBE_TIMEOUT)
                }]
              );
            }
            this._requires_ack(wireMessage);
            this._schedule_message(wireMessage);
          };
          ClientImpl.prototype.unsubscribe = function(filter, unsubscribeOptions) {
            this._trace("Client.unsubscribe", filter, unsubscribeOptions);
            if (!this.connected)
              throw new Error(format(ERROR.INVALID_STATE, ["not connected"]));
            var wireMessage = new WireMessage(MESSAGE_TYPE.UNSUBSCRIBE);
            wireMessage.topics = filter.constructor === Array ? filter : [filter];
            if (unsubscribeOptions.onSuccess) {
              wireMessage.callback = function() {
                unsubscribeOptions.onSuccess({ invocationContext: unsubscribeOptions.invocationContext });
              };
            }
            if (unsubscribeOptions.timeout) {
              wireMessage.timeOut = new Timeout(
                this,
                unsubscribeOptions.timeout,
                unsubscribeOptions.onFailure,
                [{
                  invocationContext: unsubscribeOptions.invocationContext,
                  errorCode: ERROR.UNSUBSCRIBE_TIMEOUT.code,
                  errorMessage: format(ERROR.UNSUBSCRIBE_TIMEOUT)
                }]
              );
            }
            this._requires_ack(wireMessage);
            this._schedule_message(wireMessage);
          };
          ClientImpl.prototype.send = function(message) {
            this._trace("Client.send", message);
            var wireMessage = new WireMessage(MESSAGE_TYPE.PUBLISH);
            wireMessage.payloadMessage = message;
            if (this.connected) {
              if (message.qos > 0) {
                this._requires_ack(wireMessage);
              } else if (this.onMessageDelivered) {
                this._notify_msg_sent[wireMessage] = this.onMessageDelivered(wireMessage.payloadMessage);
              }
              this._schedule_message(wireMessage);
            } else {
              if (this._reconnecting && this.disconnectedPublishing) {
                var messageCount = Object.keys(this._sentMessages).length + this._buffered_msg_queue.length;
                if (messageCount > this.disconnectedBufferSize) {
                  throw new Error(format(ERROR.BUFFER_FULL, [this.disconnectedBufferSize]));
                } else {
                  if (message.qos > 0) {
                    this._requires_ack(wireMessage);
                  } else {
                    wireMessage.sequence = ++this._sequence;
                    this._buffered_msg_queue.unshift(wireMessage);
                  }
                }
              } else {
                throw new Error(format(ERROR.INVALID_STATE, ["not connected"]));
              }
            }
          };
          ClientImpl.prototype.disconnect = function() {
            this._trace("Client.disconnect");
            if (this._reconnecting) {
              this._reconnectTimeout.cancel();
              this._reconnectTimeout = null;
              this._reconnecting = false;
            }
            if (!this.socket)
              throw new Error(format(ERROR.INVALID_STATE, ["not connecting or connected"]));
            var wireMessage = new WireMessage(MESSAGE_TYPE.DISCONNECT);
            this._notify_msg_sent[wireMessage] = scope(this._disconnected, this);
            this._schedule_message(wireMessage);
          };
          ClientImpl.prototype.getTraceLog = function() {
            if (this._traceBuffer !== null) {
              this._trace("Client.getTraceLog", /* @__PURE__ */ new Date());
              this._trace("Client.getTraceLog in flight messages", this._sentMessages.length);
              for (var key in this._sentMessages)
                this._trace("_sentMessages ", key, this._sentMessages[key]);
              for (var key in this._receivedMessages)
                this._trace("_receivedMessages ", key, this._receivedMessages[key]);
              return this._traceBuffer;
            }
          };
          ClientImpl.prototype.startTrace = function() {
            if (this._traceBuffer === null) {
              this._traceBuffer = [];
            }
            this._trace("Client.startTrace", /* @__PURE__ */ new Date(), version);
          };
          ClientImpl.prototype.stopTrace = function() {
            delete this._traceBuffer;
          };
          ClientImpl.prototype._doConnect = function(wsurl) {
            if (this.connectOptions.useSSL) {
              var uriParts = wsurl.split(":");
              uriParts[0] = "wss";
              wsurl = uriParts.join(":");
            }
            this._wsuri = wsurl;
            this.connected = false;
            if (this.connectOptions.mqttVersion < 4) {
              this.socket = new WebSocket(wsurl, ["mqttv3.1"]);
            } else {
              this.socket = new WebSocket(wsurl, ["mqtt"]);
            }
            this.socket.binaryType = "arraybuffer";
            this.socket.onopen = scope(this._on_socket_open, this);
            this.socket.onmessage = scope(this._on_socket_message, this);
            this.socket.onerror = scope(this._on_socket_error, this);
            this.socket.onclose = scope(this._on_socket_close, this);
            this.sendPinger = new Pinger(this, this.connectOptions.keepAliveInterval);
            this.receivePinger = new Pinger(this, this.connectOptions.keepAliveInterval);
            if (this._connectTimeout) {
              this._connectTimeout.cancel();
              this._connectTimeout = null;
            }
            this._connectTimeout = new Timeout(this, this.connectOptions.timeout, this._disconnected, [ERROR.CONNECT_TIMEOUT.code, format(ERROR.CONNECT_TIMEOUT)]);
          };
          ClientImpl.prototype._schedule_message = function(message) {
            this._msg_queue.unshift(message);
            if (this.connected) {
              this._process_queue();
            }
          };
          ClientImpl.prototype.store = function(prefix, wireMessage) {
            var storedMessage = { type: wireMessage.type, messageIdentifier: wireMessage.messageIdentifier, version: 1 };
            switch (wireMessage.type) {
              case MESSAGE_TYPE.PUBLISH:
                if (wireMessage.pubRecReceived)
                  storedMessage.pubRecReceived = true;
                storedMessage.payloadMessage = {};
                var hex = "";
                var messageBytes = wireMessage.payloadMessage.payloadBytes;
                for (var i = 0; i < messageBytes.length; i++) {
                  if (messageBytes[i] <= 15)
                    hex = hex + "0" + messageBytes[i].toString(16);
                  else
                    hex = hex + messageBytes[i].toString(16);
                }
                storedMessage.payloadMessage.payloadHex = hex;
                storedMessage.payloadMessage.qos = wireMessage.payloadMessage.qos;
                storedMessage.payloadMessage.destinationName = wireMessage.payloadMessage.destinationName;
                if (wireMessage.payloadMessage.duplicate)
                  storedMessage.payloadMessage.duplicate = true;
                if (wireMessage.payloadMessage.retained)
                  storedMessage.payloadMessage.retained = true;
                if (prefix.indexOf("Sent:") === 0) {
                  if (wireMessage.sequence === void 0)
                    wireMessage.sequence = ++this._sequence;
                  storedMessage.sequence = wireMessage.sequence;
                }
                break;
              default:
                throw Error(format(ERROR.INVALID_STORED_DATA, [prefix + this._localKey + wireMessage.messageIdentifier, storedMessage]));
            }
            localStorage.setItem(prefix + this._localKey + wireMessage.messageIdentifier, JSON.stringify(storedMessage));
          };
          ClientImpl.prototype.restore = function(key) {
            var value = localStorage.getItem(key);
            var storedMessage = JSON.parse(value);
            var wireMessage = new WireMessage(storedMessage.type, storedMessage);
            switch (storedMessage.type) {
              case MESSAGE_TYPE.PUBLISH:
                var hex = storedMessage.payloadMessage.payloadHex;
                var buffer = new ArrayBuffer(hex.length / 2);
                var byteStream = new Uint8Array(buffer);
                var i = 0;
                while (hex.length >= 2) {
                  var x = parseInt(hex.substring(0, 2), 16);
                  hex = hex.substring(2, hex.length);
                  byteStream[i++] = x;
                }
                var payloadMessage = new Message(byteStream);
                payloadMessage.qos = storedMessage.payloadMessage.qos;
                payloadMessage.destinationName = storedMessage.payloadMessage.destinationName;
                if (storedMessage.payloadMessage.duplicate)
                  payloadMessage.duplicate = true;
                if (storedMessage.payloadMessage.retained)
                  payloadMessage.retained = true;
                wireMessage.payloadMessage = payloadMessage;
                break;
              default:
                throw Error(format(ERROR.INVALID_STORED_DATA, [key, value]));
            }
            if (key.indexOf("Sent:" + this._localKey) === 0) {
              wireMessage.payloadMessage.duplicate = true;
              this._sentMessages[wireMessage.messageIdentifier] = wireMessage;
            } else if (key.indexOf("Received:" + this._localKey) === 0) {
              this._receivedMessages[wireMessage.messageIdentifier] = wireMessage;
            }
          };
          ClientImpl.prototype._process_queue = function() {
            var message = null;
            while (message = this._msg_queue.pop()) {
              this._socket_send(message);
              if (this._notify_msg_sent[message]) {
                this._notify_msg_sent[message]();
                delete this._notify_msg_sent[message];
              }
            }
          };
          ClientImpl.prototype._requires_ack = function(wireMessage) {
            var messageCount = Object.keys(this._sentMessages).length;
            if (messageCount > this.maxMessageIdentifier)
              throw Error("Too many messages:" + messageCount);
            while (this._sentMessages[this._message_identifier] !== void 0) {
              this._message_identifier++;
            }
            wireMessage.messageIdentifier = this._message_identifier;
            this._sentMessages[wireMessage.messageIdentifier] = wireMessage;
            if (wireMessage.type === MESSAGE_TYPE.PUBLISH) {
              this.store("Sent:", wireMessage);
            }
            if (this._message_identifier === this.maxMessageIdentifier) {
              this._message_identifier = 1;
            }
          };
          ClientImpl.prototype._on_socket_open = function() {
            var wireMessage = new WireMessage(MESSAGE_TYPE.CONNECT, this.connectOptions);
            wireMessage.clientId = this.clientId;
            this._socket_send(wireMessage);
          };
          ClientImpl.prototype._on_socket_message = function(event) {
            this._trace("Client._on_socket_message", event.data);
            var messages = this._deframeMessages(event.data);
            for (var i = 0; i < messages.length; i += 1) {
              this._handleMessage(messages[i]);
            }
          };
          ClientImpl.prototype._deframeMessages = function(data) {
            var byteArray = new Uint8Array(data);
            var messages = [];
            if (this.receiveBuffer) {
              var newData = new Uint8Array(this.receiveBuffer.length + byteArray.length);
              newData.set(this.receiveBuffer);
              newData.set(byteArray, this.receiveBuffer.length);
              byteArray = newData;
              delete this.receiveBuffer;
            }
            try {
              var offset = 0;
              while (offset < byteArray.length) {
                var result = decodeMessage(byteArray, offset);
                var wireMessage = result[0];
                offset = result[1];
                if (wireMessage !== null) {
                  messages.push(wireMessage);
                } else {
                  break;
                }
              }
              if (offset < byteArray.length) {
                this.receiveBuffer = byteArray.subarray(offset);
              }
            } catch (error) {
              var errorStack = error.hasOwnProperty("stack") == "undefined" ? error.stack.toString() : "No Error Stack Available";
              this._disconnected(ERROR.INTERNAL_ERROR.code, format(ERROR.INTERNAL_ERROR, [error.message, errorStack]));
              return;
            }
            return messages;
          };
          ClientImpl.prototype._handleMessage = function(wireMessage) {
            this._trace("Client._handleMessage", wireMessage);
            try {
              switch (wireMessage.type) {
                case MESSAGE_TYPE.CONNACK:
                  this._connectTimeout.cancel();
                  if (this._reconnectTimeout)
                    this._reconnectTimeout.cancel();
                  if (this.connectOptions.cleanSession) {
                    for (var key in this._sentMessages) {
                      var sentMessage = this._sentMessages[key];
                      localStorage.removeItem("Sent:" + this._localKey + sentMessage.messageIdentifier);
                    }
                    this._sentMessages = {};
                    for (var key in this._receivedMessages) {
                      var receivedMessage = this._receivedMessages[key];
                      localStorage.removeItem("Received:" + this._localKey + receivedMessage.messageIdentifier);
                    }
                    this._receivedMessages = {};
                  }
                  if (wireMessage.returnCode === 0) {
                    this.connected = true;
                    if (this.connectOptions.uris)
                      this.hostIndex = this.connectOptions.uris.length;
                  } else {
                    this._disconnected(ERROR.CONNACK_RETURNCODE.code, format(ERROR.CONNACK_RETURNCODE, [wireMessage.returnCode, CONNACK_RC[wireMessage.returnCode]]));
                    break;
                  }
                  var sequencedMessages = [];
                  for (var msgId in this._sentMessages) {
                    if (this._sentMessages.hasOwnProperty(msgId))
                      sequencedMessages.push(this._sentMessages[msgId]);
                  }
                  if (this._buffered_msg_queue.length > 0) {
                    var msg = null;
                    while (msg = this._buffered_msg_queue.pop()) {
                      sequencedMessages.push(msg);
                      if (this.onMessageDelivered)
                        this._notify_msg_sent[msg] = this.onMessageDelivered(msg.payloadMessage);
                    }
                  }
                  var sequencedMessages = sequencedMessages.sort(function(a, b) {
                    return a.sequence - b.sequence;
                  });
                  for (var i = 0, len = sequencedMessages.length; i < len; i++) {
                    var sentMessage = sequencedMessages[i];
                    if (sentMessage.type == MESSAGE_TYPE.PUBLISH && sentMessage.pubRecReceived) {
                      var pubRelMessage = new WireMessage(MESSAGE_TYPE.PUBREL, { messageIdentifier: sentMessage.messageIdentifier });
                      this._schedule_message(pubRelMessage);
                    } else {
                      this._schedule_message(sentMessage);
                    }
                  }
                  if (this.connectOptions.onSuccess) {
                    this.connectOptions.onSuccess({ invocationContext: this.connectOptions.invocationContext });
                  }
                  var reconnected = false;
                  if (this._reconnecting) {
                    reconnected = true;
                    this._reconnectInterval = 1;
                    this._reconnecting = false;
                  }
                  this._connected(reconnected, this._wsuri);
                  this._process_queue();
                  break;
                case MESSAGE_TYPE.PUBLISH:
                  this._receivePublish(wireMessage);
                  break;
                case MESSAGE_TYPE.PUBACK:
                  var sentMessage = this._sentMessages[wireMessage.messageIdentifier];
                  if (sentMessage) {
                    delete this._sentMessages[wireMessage.messageIdentifier];
                    localStorage.removeItem("Sent:" + this._localKey + wireMessage.messageIdentifier);
                    if (this.onMessageDelivered)
                      this.onMessageDelivered(sentMessage.payloadMessage);
                  }
                  break;
                case MESSAGE_TYPE.PUBREC:
                  var sentMessage = this._sentMessages[wireMessage.messageIdentifier];
                  if (sentMessage) {
                    sentMessage.pubRecReceived = true;
                    var pubRelMessage = new WireMessage(MESSAGE_TYPE.PUBREL, { messageIdentifier: wireMessage.messageIdentifier });
                    this.store("Sent:", sentMessage);
                    this._schedule_message(pubRelMessage);
                  }
                  break;
                case MESSAGE_TYPE.PUBREL:
                  var receivedMessage = this._receivedMessages[wireMessage.messageIdentifier];
                  localStorage.removeItem("Received:" + this._localKey + wireMessage.messageIdentifier);
                  if (receivedMessage) {
                    this._receiveMessage(receivedMessage);
                    delete this._receivedMessages[wireMessage.messageIdentifier];
                  }
                  var pubCompMessage = new WireMessage(MESSAGE_TYPE.PUBCOMP, { messageIdentifier: wireMessage.messageIdentifier });
                  this._schedule_message(pubCompMessage);
                  break;
                case MESSAGE_TYPE.PUBCOMP:
                  var sentMessage = this._sentMessages[wireMessage.messageIdentifier];
                  delete this._sentMessages[wireMessage.messageIdentifier];
                  localStorage.removeItem("Sent:" + this._localKey + wireMessage.messageIdentifier);
                  if (this.onMessageDelivered)
                    this.onMessageDelivered(sentMessage.payloadMessage);
                  break;
                case MESSAGE_TYPE.SUBACK:
                  var sentMessage = this._sentMessages[wireMessage.messageIdentifier];
                  if (sentMessage) {
                    if (sentMessage.timeOut)
                      sentMessage.timeOut.cancel();
                    if (wireMessage.returnCode[0] === 128) {
                      if (sentMessage.onFailure) {
                        sentMessage.onFailure(wireMessage.returnCode);
                      }
                    } else if (sentMessage.onSuccess) {
                      sentMessage.onSuccess(wireMessage.returnCode);
                    }
                    delete this._sentMessages[wireMessage.messageIdentifier];
                  }
                  break;
                case MESSAGE_TYPE.UNSUBACK:
                  var sentMessage = this._sentMessages[wireMessage.messageIdentifier];
                  if (sentMessage) {
                    if (sentMessage.timeOut)
                      sentMessage.timeOut.cancel();
                    if (sentMessage.callback) {
                      sentMessage.callback();
                    }
                    delete this._sentMessages[wireMessage.messageIdentifier];
                  }
                  break;
                case MESSAGE_TYPE.PINGRESP:
                  this.sendPinger.reset();
                  break;
                case MESSAGE_TYPE.DISCONNECT:
                  this._disconnected(ERROR.INVALID_MQTT_MESSAGE_TYPE.code, format(ERROR.INVALID_MQTT_MESSAGE_TYPE, [wireMessage.type]));
                  break;
                default:
                  this._disconnected(ERROR.INVALID_MQTT_MESSAGE_TYPE.code, format(ERROR.INVALID_MQTT_MESSAGE_TYPE, [wireMessage.type]));
              }
            } catch (error) {
              var errorStack = error.hasOwnProperty("stack") == "undefined" ? error.stack.toString() : "No Error Stack Available";
              this._disconnected(ERROR.INTERNAL_ERROR.code, format(ERROR.INTERNAL_ERROR, [error.message, errorStack]));
              return;
            }
          };
          ClientImpl.prototype._on_socket_error = function(error) {
            if (!this._reconnecting) {
              this._disconnected(ERROR.SOCKET_ERROR.code, format(ERROR.SOCKET_ERROR, [error.data]));
            }
          };
          ClientImpl.prototype._on_socket_close = function() {
            if (!this._reconnecting) {
              this._disconnected(ERROR.SOCKET_CLOSE.code, format(ERROR.SOCKET_CLOSE));
            }
          };
          ClientImpl.prototype._socket_send = function(wireMessage) {
            if (wireMessage.type == 1) {
              var wireMessageMasked = this._traceMask(wireMessage, "password");
              this._trace("Client._socket_send", wireMessageMasked);
            } else this._trace("Client._socket_send", wireMessage);
            this.socket.send(wireMessage.encode());
            this.sendPinger.reset();
          };
          ClientImpl.prototype._receivePublish = function(wireMessage) {
            switch (wireMessage.payloadMessage.qos) {
              case "undefined":
              case 0:
                this._receiveMessage(wireMessage);
                break;
              case 1:
                var pubAckMessage = new WireMessage(MESSAGE_TYPE.PUBACK, { messageIdentifier: wireMessage.messageIdentifier });
                this._schedule_message(pubAckMessage);
                this._receiveMessage(wireMessage);
                break;
              case 2:
                this._receivedMessages[wireMessage.messageIdentifier] = wireMessage;
                this.store("Received:", wireMessage);
                var pubRecMessage = new WireMessage(MESSAGE_TYPE.PUBREC, { messageIdentifier: wireMessage.messageIdentifier });
                this._schedule_message(pubRecMessage);
                break;
              default:
                throw Error("Invaild qos=" + wireMessage.payloadMessage.qos);
            }
          };
          ClientImpl.prototype._receiveMessage = function(wireMessage) {
            if (this.onMessageArrived) {
              this.onMessageArrived(wireMessage.payloadMessage);
            }
          };
          ClientImpl.prototype._connected = function(reconnect, uri) {
            if (this.onConnected)
              this.onConnected(reconnect, uri);
          };
          ClientImpl.prototype._reconnect = function() {
            this._trace("Client._reconnect");
            if (!this.connected) {
              this._reconnecting = true;
              this.sendPinger.cancel();
              this.receivePinger.cancel();
              if (this._reconnectInterval < 128)
                this._reconnectInterval = this._reconnectInterval * 2;
              if (this.connectOptions.uris) {
                this.hostIndex = 0;
                this._doConnect(this.connectOptions.uris[0]);
              } else {
                this._doConnect(this.uri);
              }
            }
          };
          ClientImpl.prototype._disconnected = function(errorCode, errorText) {
            this._trace("Client._disconnected", errorCode, errorText);
            if (errorCode !== void 0 && this._reconnecting) {
              this._reconnectTimeout = new Timeout(this, this._reconnectInterval, this._reconnect);
              return;
            }
            this.sendPinger.cancel();
            this.receivePinger.cancel();
            if (this._connectTimeout) {
              this._connectTimeout.cancel();
              this._connectTimeout = null;
            }
            this._msg_queue = [];
            this._buffered_msg_queue = [];
            this._notify_msg_sent = {};
            if (this.socket) {
              this.socket.onopen = null;
              this.socket.onmessage = null;
              this.socket.onerror = null;
              this.socket.onclose = null;
              if (this.socket.readyState === 1)
                this.socket.close();
              delete this.socket;
            }
            if (this.connectOptions.uris && this.hostIndex < this.connectOptions.uris.length - 1) {
              this.hostIndex++;
              this._doConnect(this.connectOptions.uris[this.hostIndex]);
            } else {
              if (errorCode === void 0) {
                errorCode = ERROR.OK.code;
                errorText = format(ERROR.OK);
              }
              if (this.connected) {
                this.connected = false;
                if (this.onConnectionLost) {
                  this.onConnectionLost({ errorCode, errorMessage: errorText, reconnect: this.connectOptions.reconnect, uri: this._wsuri });
                }
                if (errorCode !== ERROR.OK.code && this.connectOptions.reconnect) {
                  this._reconnectInterval = 1;
                  this._reconnect();
                  return;
                }
              } else {
                if (this.connectOptions.mqttVersion === 4 && this.connectOptions.mqttVersionExplicit === false) {
                  this._trace("Failed to connect V4, dropping back to V3");
                  this.connectOptions.mqttVersion = 3;
                  if (this.connectOptions.uris) {
                    this.hostIndex = 0;
                    this._doConnect(this.connectOptions.uris[0]);
                  } else {
                    this._doConnect(this.uri);
                  }
                } else if (this.connectOptions.onFailure) {
                  this.connectOptions.onFailure({ invocationContext: this.connectOptions.invocationContext, errorCode, errorMessage: errorText });
                }
              }
            }
          };
          ClientImpl.prototype._trace = function() {
            if (this.traceFunction) {
              var args = Array.prototype.slice.call(arguments);
              for (var i in args) {
                if (typeof args[i] !== "undefined")
                  args.splice(i, 1, JSON.stringify(args[i]));
              }
              var record = args.join("");
              this.traceFunction({ severity: "Debug", message: record });
            }
            if (this._traceBuffer !== null) {
              for (var i = 0, max = arguments.length; i < max; i++) {
                if (this._traceBuffer.length == this._MAX_TRACE_ENTRIES) {
                  this._traceBuffer.shift();
                }
                if (i === 0) this._traceBuffer.push(arguments[i]);
                else if (typeof arguments[i] === "undefined") this._traceBuffer.push(arguments[i]);
                else this._traceBuffer.push("  " + JSON.stringify(arguments[i]));
              }
            }
          };
          ClientImpl.prototype._traceMask = function(traceObject, masked) {
            var traceObjectMasked = {};
            for (var attr in traceObject) {
              if (traceObject.hasOwnProperty(attr)) {
                if (attr == masked)
                  traceObjectMasked[attr] = "******";
                else
                  traceObjectMasked[attr] = traceObject[attr];
              }
            }
            return traceObjectMasked;
          };
          var Client = function(host, port, path, clientId) {
            var uri;
            if (typeof host !== "string")
              throw new Error(format(ERROR.INVALID_TYPE, [typeof host, "host"]));
            if (arguments.length == 2) {
              clientId = port;
              uri = host;
              var match = uri.match(/^(wss?):\/\/((\[(.+)\])|([^\/]+?))(:(\d+))?(\/.*)$/);
              if (match) {
                host = match[4] || match[2];
                port = parseInt(match[7]);
                path = match[8];
              } else {
                throw new Error(format(ERROR.INVALID_ARGUMENT, [host, "host"]));
              }
            } else {
              if (arguments.length == 3) {
                clientId = path;
                path = "/mqtt";
              }
              if (typeof port !== "number" || port < 0)
                throw new Error(format(ERROR.INVALID_TYPE, [typeof port, "port"]));
              if (typeof path !== "string")
                throw new Error(format(ERROR.INVALID_TYPE, [typeof path, "path"]));
              var ipv6AddSBracket = host.indexOf(":") !== -1 && host.slice(0, 1) !== "[" && host.slice(-1) !== "]";
              uri = "ws://" + (ipv6AddSBracket ? "[" + host + "]" : host) + ":" + port + path;
            }
            var clientIdLength = 0;
            for (var i = 0; i < clientId.length; i++) {
              var charCode = clientId.charCodeAt(i);
              if (55296 <= charCode && charCode <= 56319) {
                i++;
              }
              clientIdLength++;
            }
            if (typeof clientId !== "string" || clientIdLength > 65535)
              throw new Error(format(ERROR.INVALID_ARGUMENT, [clientId, "clientId"]));
            var client = new ClientImpl(uri, host, port, path, clientId);
            Object.defineProperties(this, {
              "host": {
                get: function() {
                  return host;
                },
                set: function() {
                  throw new Error(format(ERROR.UNSUPPORTED_OPERATION));
                }
              },
              "port": {
                get: function() {
                  return port;
                },
                set: function() {
                  throw new Error(format(ERROR.UNSUPPORTED_OPERATION));
                }
              },
              "path": {
                get: function() {
                  return path;
                },
                set: function() {
                  throw new Error(format(ERROR.UNSUPPORTED_OPERATION));
                }
              },
              "uri": {
                get: function() {
                  return uri;
                },
                set: function() {
                  throw new Error(format(ERROR.UNSUPPORTED_OPERATION));
                }
              },
              "clientId": {
                get: function() {
                  return client.clientId;
                },
                set: function() {
                  throw new Error(format(ERROR.UNSUPPORTED_OPERATION));
                }
              },
              "onConnected": {
                get: function() {
                  return client.onConnected;
                },
                set: function(newOnConnected) {
                  if (typeof newOnConnected === "function")
                    client.onConnected = newOnConnected;
                  else
                    throw new Error(format(ERROR.INVALID_TYPE, [typeof newOnConnected, "onConnected"]));
                }
              },
              "disconnectedPublishing": {
                get: function() {
                  return client.disconnectedPublishing;
                },
                set: function(newDisconnectedPublishing) {
                  client.disconnectedPublishing = newDisconnectedPublishing;
                }
              },
              "disconnectedBufferSize": {
                get: function() {
                  return client.disconnectedBufferSize;
                },
                set: function(newDisconnectedBufferSize) {
                  client.disconnectedBufferSize = newDisconnectedBufferSize;
                }
              },
              "onConnectionLost": {
                get: function() {
                  return client.onConnectionLost;
                },
                set: function(newOnConnectionLost) {
                  if (typeof newOnConnectionLost === "function")
                    client.onConnectionLost = newOnConnectionLost;
                  else
                    throw new Error(format(ERROR.INVALID_TYPE, [typeof newOnConnectionLost, "onConnectionLost"]));
                }
              },
              "onMessageDelivered": {
                get: function() {
                  return client.onMessageDelivered;
                },
                set: function(newOnMessageDelivered) {
                  if (typeof newOnMessageDelivered === "function")
                    client.onMessageDelivered = newOnMessageDelivered;
                  else
                    throw new Error(format(ERROR.INVALID_TYPE, [typeof newOnMessageDelivered, "onMessageDelivered"]));
                }
              },
              "onMessageArrived": {
                get: function() {
                  return client.onMessageArrived;
                },
                set: function(newOnMessageArrived) {
                  if (typeof newOnMessageArrived === "function")
                    client.onMessageArrived = newOnMessageArrived;
                  else
                    throw new Error(format(ERROR.INVALID_TYPE, [typeof newOnMessageArrived, "onMessageArrived"]));
                }
              },
              "trace": {
                get: function() {
                  return client.traceFunction;
                },
                set: function(trace) {
                  if (typeof trace === "function") {
                    client.traceFunction = trace;
                  } else {
                    throw new Error(format(ERROR.INVALID_TYPE, [typeof trace, "onTrace"]));
                  }
                }
              }
            });
            this.connect = function(connectOptions) {
              connectOptions = connectOptions || {};
              validate(connectOptions, {
                timeout: "number",
                userName: "string",
                password: "string",
                willMessage: "object",
                keepAliveInterval: "number",
                cleanSession: "boolean",
                useSSL: "boolean",
                invocationContext: "object",
                onSuccess: "function",
                onFailure: "function",
                hosts: "object",
                ports: "object",
                reconnect: "boolean",
                mqttVersion: "number",
                mqttVersionExplicit: "boolean",
                uris: "object"
              });
              if (connectOptions.keepAliveInterval === void 0)
                connectOptions.keepAliveInterval = 60;
              if (connectOptions.mqttVersion > 4 || connectOptions.mqttVersion < 3) {
                throw new Error(format(ERROR.INVALID_ARGUMENT, [connectOptions.mqttVersion, "connectOptions.mqttVersion"]));
              }
              if (connectOptions.mqttVersion === void 0) {
                connectOptions.mqttVersionExplicit = false;
                connectOptions.mqttVersion = 4;
              } else {
                connectOptions.mqttVersionExplicit = true;
              }
              if (connectOptions.password !== void 0 && connectOptions.userName === void 0)
                throw new Error(format(ERROR.INVALID_ARGUMENT, [connectOptions.password, "connectOptions.password"]));
              if (connectOptions.willMessage) {
                if (!(connectOptions.willMessage instanceof Message))
                  throw new Error(format(ERROR.INVALID_TYPE, [connectOptions.willMessage, "connectOptions.willMessage"]));
                connectOptions.willMessage.stringPayload = null;
                if (typeof connectOptions.willMessage.destinationName === "undefined")
                  throw new Error(format(ERROR.INVALID_TYPE, [typeof connectOptions.willMessage.destinationName, "connectOptions.willMessage.destinationName"]));
              }
              if (typeof connectOptions.cleanSession === "undefined")
                connectOptions.cleanSession = true;
              if (connectOptions.hosts) {
                if (!(connectOptions.hosts instanceof Array))
                  throw new Error(format(ERROR.INVALID_ARGUMENT, [connectOptions.hosts, "connectOptions.hosts"]));
                if (connectOptions.hosts.length < 1)
                  throw new Error(format(ERROR.INVALID_ARGUMENT, [connectOptions.hosts, "connectOptions.hosts"]));
                var usingURIs = false;
                for (var i2 = 0; i2 < connectOptions.hosts.length; i2++) {
                  if (typeof connectOptions.hosts[i2] !== "string")
                    throw new Error(format(ERROR.INVALID_TYPE, [typeof connectOptions.hosts[i2], "connectOptions.hosts[" + i2 + "]"]));
                  if (/^(wss?):\/\/((\[(.+)\])|([^\/]+?))(:(\d+))?(\/.*)$/.test(connectOptions.hosts[i2])) {
                    if (i2 === 0) {
                      usingURIs = true;
                    } else if (!usingURIs) {
                      throw new Error(format(ERROR.INVALID_ARGUMENT, [connectOptions.hosts[i2], "connectOptions.hosts[" + i2 + "]"]));
                    }
                  } else if (usingURIs) {
                    throw new Error(format(ERROR.INVALID_ARGUMENT, [connectOptions.hosts[i2], "connectOptions.hosts[" + i2 + "]"]));
                  }
                }
                if (!usingURIs) {
                  if (!connectOptions.ports)
                    throw new Error(format(ERROR.INVALID_ARGUMENT, [connectOptions.ports, "connectOptions.ports"]));
                  if (!(connectOptions.ports instanceof Array))
                    throw new Error(format(ERROR.INVALID_ARGUMENT, [connectOptions.ports, "connectOptions.ports"]));
                  if (connectOptions.hosts.length !== connectOptions.ports.length)
                    throw new Error(format(ERROR.INVALID_ARGUMENT, [connectOptions.ports, "connectOptions.ports"]));
                  connectOptions.uris = [];
                  for (var i2 = 0; i2 < connectOptions.hosts.length; i2++) {
                    if (typeof connectOptions.ports[i2] !== "number" || connectOptions.ports[i2] < 0)
                      throw new Error(format(ERROR.INVALID_TYPE, [typeof connectOptions.ports[i2], "connectOptions.ports[" + i2 + "]"]));
                    var host2 = connectOptions.hosts[i2];
                    var port2 = connectOptions.ports[i2];
                    var ipv6 = host2.indexOf(":") !== -1;
                    uri = "ws://" + (ipv6 ? "[" + host2 + "]" : host2) + ":" + port2 + path;
                    connectOptions.uris.push(uri);
                  }
                } else {
                  connectOptions.uris = connectOptions.hosts;
                }
              }
              client.connect(connectOptions);
            };
            this.subscribe = function(filter, subscribeOptions) {
              if (typeof filter !== "string" && filter.constructor !== Array)
                throw new Error("Invalid argument:" + filter);
              subscribeOptions = subscribeOptions || {};
              validate(subscribeOptions, {
                qos: "number",
                invocationContext: "object",
                onSuccess: "function",
                onFailure: "function",
                timeout: "number"
              });
              if (subscribeOptions.timeout && !subscribeOptions.onFailure)
                throw new Error("subscribeOptions.timeout specified with no onFailure callback.");
              if (typeof subscribeOptions.qos !== "undefined" && !(subscribeOptions.qos === 0 || subscribeOptions.qos === 1 || subscribeOptions.qos === 2))
                throw new Error(format(ERROR.INVALID_ARGUMENT, [subscribeOptions.qos, "subscribeOptions.qos"]));
              client.subscribe(filter, subscribeOptions);
            };
            this.unsubscribe = function(filter, unsubscribeOptions) {
              if (typeof filter !== "string" && filter.constructor !== Array)
                throw new Error("Invalid argument:" + filter);
              unsubscribeOptions = unsubscribeOptions || {};
              validate(unsubscribeOptions, {
                invocationContext: "object",
                onSuccess: "function",
                onFailure: "function",
                timeout: "number"
              });
              if (unsubscribeOptions.timeout && !unsubscribeOptions.onFailure)
                throw new Error("unsubscribeOptions.timeout specified with no onFailure callback.");
              client.unsubscribe(filter, unsubscribeOptions);
            };
            this.send = function(topic, payload, qos, retained) {
              var message;
              if (arguments.length === 0) {
                throw new Error("Invalid argument.length");
              } else if (arguments.length == 1) {
                if (!(topic instanceof Message) && typeof topic !== "string")
                  throw new Error("Invalid argument:" + typeof topic);
                message = topic;
                if (typeof message.destinationName === "undefined")
                  throw new Error(format(ERROR.INVALID_ARGUMENT, [message.destinationName, "Message.destinationName"]));
                client.send(message);
              } else {
                message = new Message(payload);
                message.destinationName = topic;
                if (arguments.length >= 3)
                  message.qos = qos;
                if (arguments.length >= 4)
                  message.retained = retained;
                client.send(message);
              }
            };
            this.publish = function(topic, payload, qos, retained) {
              var message;
              if (arguments.length === 0) {
                throw new Error("Invalid argument.length");
              } else if (arguments.length == 1) {
                if (!(topic instanceof Message) && typeof topic !== "string")
                  throw new Error("Invalid argument:" + typeof topic);
                message = topic;
                if (typeof message.destinationName === "undefined")
                  throw new Error(format(ERROR.INVALID_ARGUMENT, [message.destinationName, "Message.destinationName"]));
                client.send(message);
              } else {
                message = new Message(payload);
                message.destinationName = topic;
                if (arguments.length >= 3)
                  message.qos = qos;
                if (arguments.length >= 4)
                  message.retained = retained;
                client.send(message);
              }
            };
            this.disconnect = function() {
              client.disconnect();
            };
            this.getTraceLog = function() {
              return client.getTraceLog();
            };
            this.startTrace = function() {
              client.startTrace();
            };
            this.stopTrace = function() {
              client.stopTrace();
            };
            this.isConnected = function() {
              return client.connected;
            };
          };
          var Message = function(newPayload) {
            var payload;
            if (typeof newPayload === "string" || newPayload instanceof ArrayBuffer || ArrayBuffer.isView(newPayload) && !(newPayload instanceof DataView)) {
              payload = newPayload;
            } else {
              throw format(ERROR.INVALID_ARGUMENT, [newPayload, "newPayload"]);
            }
            var destinationName;
            var qos = 0;
            var retained = false;
            var duplicate = false;
            Object.defineProperties(this, {
              "payloadString": {
                enumerable: true,
                get: function() {
                  if (typeof payload === "string")
                    return payload;
                  else
                    return parseUTF8(payload, 0, payload.length);
                }
              },
              "payloadBytes": {
                enumerable: true,
                get: function() {
                  if (typeof payload === "string") {
                    var buffer = new ArrayBuffer(UTF8Length(payload));
                    var byteStream = new Uint8Array(buffer);
                    stringToUTF8(payload, byteStream, 0);
                    return byteStream;
                  } else {
                    return payload;
                  }
                }
              },
              "destinationName": {
                enumerable: true,
                get: function() {
                  return destinationName;
                },
                set: function(newDestinationName) {
                  if (typeof newDestinationName === "string")
                    destinationName = newDestinationName;
                  else
                    throw new Error(format(ERROR.INVALID_ARGUMENT, [newDestinationName, "newDestinationName"]));
                }
              },
              "qos": {
                enumerable: true,
                get: function() {
                  return qos;
                },
                set: function(newQos) {
                  if (newQos === 0 || newQos === 1 || newQos === 2)
                    qos = newQos;
                  else
                    throw new Error("Invalid argument:" + newQos);
                }
              },
              "retained": {
                enumerable: true,
                get: function() {
                  return retained;
                },
                set: function(newRetained) {
                  if (typeof newRetained === "boolean")
                    retained = newRetained;
                  else
                    throw new Error(format(ERROR.INVALID_ARGUMENT, [newRetained, "newRetained"]));
                }
              },
              "topic": {
                enumerable: true,
                get: function() {
                  return destinationName;
                },
                set: function(newTopic) {
                  destinationName = newTopic;
                }
              },
              "duplicate": {
                enumerable: true,
                get: function() {
                  return duplicate;
                },
                set: function(newDuplicate) {
                  duplicate = newDuplicate;
                }
              }
            });
          };
          return {
            Client,
            Message
          };
        })(typeof global !== "undefined" ? global : typeof self !== "undefined" ? self : typeof window !== "undefined" ? window : {});
        return PahoMQTT;
      });
    }
  });

  // src/audio.ts
  function micAudioConstraints() {
    return {
      channelCount: 1,
      echoCancellation: { ideal: true },
      noiseSuppression: { ideal: true },
      autoGainControl: { ideal: true },
      // Chromium 120+ — isolates speech from keyboard / room noise.
      // Cast: not yet in every lib.dom MediaTrackConstraints typing.
      ...{ voiceIsolation: { ideal: true } }
    };
  }
  function startVadGate(stream) {
    const track = stream.getAudioTracks()[0] ?? null;
    let userMuted = false;
    let speaking = false;
    let hangoverUntil = 0;
    let timer = 0;
    let ctx = null;
    let source = null;
    let analyser = null;
    let probe = null;
    const SPEECH_RMS = 0.018;
    const SILENCE_RMS = 0.01;
    const HANGOVER_MS = 380;
    const POLL_MS = 50;
    const apply = () => {
      if (!track) return;
      const open = !userMuted && (speaking || performance.now() < hangoverUntil);
      if (track.enabled !== open) track.enabled = open;
    };
    try {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (AC && track) {
        ctx = new AC();
        probe = stream.clone();
        for (const t of probe.getAudioTracks()) t.enabled = true;
        source = ctx.createMediaStreamSource(probe);
        analyser = ctx.createAnalyser();
        analyser.fftSize = 512;
        analyser.smoothingTimeConstant = 0.55;
        source.connect(analyser);
        const buf = new Float32Array(analyser.fftSize);
        timer = window.setInterval(() => {
          if (!analyser || userMuted) {
            speaking = false;
            apply();
            return;
          }
          if (document.hidden) {
            apply();
            return;
          }
          analyser.getFloatTimeDomainData(buf);
          let sum = 0;
          for (let i = 0; i < buf.length; i++) {
            const v = buf[i];
            sum += v * v;
          }
          const rms = Math.sqrt(sum / buf.length);
          if (rms >= SPEECH_RMS) {
            speaking = true;
            hangoverUntil = performance.now() + HANGOVER_MS;
          } else if (rms <= SILENCE_RMS) {
            speaking = false;
          }
          apply();
        }, POLL_MS);
      }
    } catch {
    }
    apply();
    return {
      setUserMuted(muted) {
        userMuted = muted;
        if (muted) {
          speaking = false;
          hangoverUntil = 0;
        }
        apply();
      },
      refresh: apply,
      stop() {
        window.clearInterval(timer);
        timer = 0;
        try {
          source?.disconnect();
        } catch {
        }
        try {
          void ctx?.close();
        } catch {
        }
        if (probe) {
          for (const t of probe.getTracks()) t.stop();
          probe = null;
        }
        source = null;
        analyser = null;
        ctx = null;
        if (track && !userMuted) track.enabled = true;
      }
    };
  }
  var cueCtx = null;
  function ensureCueCtx() {
    try {
      if (cueCtx && cueCtx.state !== "closed") return cueCtx;
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return null;
      cueCtx = new AC();
      return cueCtx;
    } catch {
      return null;
    }
  }
  async function playMuteCue(muted) {
    const ctx = ensureCueCtx();
    if (!ctx) return;
    try {
      if (ctx.state === "suspended") await ctx.resume();
    } catch {
      return;
    }
    const now = ctx.currentTime;
    const freqs = muted ? [660, 440] : [520, 780];
    const gain = ctx.createGain();
    gain.gain.setValueAtTime(1e-4, now);
    gain.gain.exponentialRampToValueAtTime(0.07, now + 0.02);
    gain.gain.exponentialRampToValueAtTime(1e-4, now + 0.22);
    gain.connect(ctx.destination);
    for (let i = 0; i < freqs.length; i++) {
      const osc = ctx.createOscillator();
      osc.type = "sine";
      osc.frequency.value = freqs[i];
      const g = ctx.createGain();
      const t0 = now + i * 0.09;
      g.gain.setValueAtTime(1e-4, t0);
      g.gain.exponentialRampToValueAtTime(0.9, t0 + 0.015);
      g.gain.exponentialRampToValueAtTime(1e-4, t0 + 0.1);
      osc.connect(g);
      g.connect(gain);
      osc.start(t0);
      osc.stop(t0 + 0.12);
    }
  }

  // src/signaling.ts
  var import_paho_mqtt = __toESM(require_paho_mqtt());
  var MQTT_BROKERS = [
    { host: "broker.emqx.io", port: 8084, path: "/mqtt", useSSL: true },
    { host: "broker.hivemq.com", port: 8884, path: "/mqtt", useSSL: true },
    { host: "test.mosquitto.org", port: 8081, path: "/mqtt", useSSL: true }
  ];
  function randomCode(len = 6) {
    const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    let res = "";
    for (let i = 0; i < len; i++) {
      res += chars[Math.floor(Math.random() * chars.length)];
    }
    return res;
  }
  function randomId() {
    return Array.from(crypto.getRandomValues(new Uint8Array(8))).map((b) => b.toString(16).padStart(2, "0")).join("");
  }
  var DecentralizedSignalingChannel = class {
    constructor(callbacks) {
      this.client = null;
      this.localWs = null;
      this.selfId = randomId();
      this.displayName = "Guest";
      this.sessionCode = null;
      this.members = /* @__PURE__ */ new Map();
      this.useLocalFallback = false;
      this.callbacks = callbacks;
    }
    setDisplayName(name) {
      this.displayName = name || "Guest";
    }
    getSelfId() {
      return this.selfId;
    }
    async start(create, code, localWsUrl) {
      this.selfId = randomId();
      this.members.clear();
      this.members.set(this.selfId, this.displayName);
      if (create) {
        this.sessionCode = randomCode(6);
      } else {
        this.sessionCode = (code || "").trim().toUpperCase();
        if (!this.sessionCode) {
          throw new Error("Missing session code");
        }
      }
      try {
        await this.connectMqtt();
      } catch (mqttErr) {
        console.warn("[Voice] Public MQTT signaling failed, trying local fallback:", mqttErr);
        if (localWsUrl) {
          await this.connectLocalWs(localWsUrl, create);
          return;
        }
        throw mqttErr;
      }
      if (create) {
        this.callbacks.onCreated(this.sessionCode, this.selfId);
      } else {
        this.callbacks.onJoined(this.sessionCode, this.selfId);
        this.broadcastPresence("join");
      }
    }
    connectMqtt() {
      return new Promise((resolve, reject) => {
        let connected = false;
        let brokerIdx = 0;
        const tryNextBroker = () => {
          if (brokerIdx >= MQTT_BROKERS.length) {
            reject(new Error("All public signaling brokers unreachable"));
            return;
          }
          const broker = MQTT_BROKERS[brokerIdx++];
          const clientId = `ardel_voice_${this.selfId}_${Math.floor(Math.random() * 1e4)}`;
          try {
            const client = new import_paho_mqtt.default.Client(broker.host, broker.port, broker.path, clientId);
            this.client = client;
            client.onConnectionLost = (resp) => {
              console.warn("[Voice] MQTT connection lost:", resp.errorMessage);
              if (connected) {
                this.callbacks.onDisconnect();
              }
            };
            client.onMessageArrived = (msg) => {
              this.handleMqttMessage(msg.destinationName, msg.payloadString);
            };
            client.connect({
              useSSL: broker.useSSL,
              timeout: 5,
              keepAliveInterval: 30,
              cleanSession: true,
              onSuccess: () => {
                connected = true;
                this.subscribeTopics();
                resolve();
              },
              onFailure: (err) => {
                console.warn(`[Voice] MQTT broker ${broker.host} connect failed:`, err.errorMessage);
                tryNextBroker();
              }
            });
          } catch (e) {
            tryNextBroker();
          }
        };
        tryNextBroker();
      });
    }
    subscribeTopics() {
      if (!this.client || !this.sessionCode) return;
      const topicPresence = `ardel/v1/voice/${this.sessionCode}/presence`;
      const topicSignal = `ardel/v1/voice/${this.sessionCode}/signal`;
      this.client.subscribe(topicPresence, { qos: 1 });
      this.client.subscribe(topicSignal, { qos: 1 });
    }
    handleMqttMessage(topic, payloadStr) {
      let data;
      try {
        data = JSON.parse(payloadStr);
      } catch {
        return;
      }
      if (data.from === this.selfId) {
        return;
      }
      if (topic.endsWith("/presence")) {
        if (data.type === "join") {
          if (data.peerId && data.peerId !== this.selfId) {
            this.members.set(data.peerId, data.name || "Guest");
            this.broadcastPresence("presence");
            this.emitPeers();
          }
        } else if (data.type === "presence") {
          if (data.peerId && data.peerId !== this.selfId) {
            this.members.set(data.peerId, data.name || "Guest");
            this.emitPeers();
          }
        } else if (data.type === "leave") {
          if (data.peerId) {
            this.members.delete(data.peerId);
            this.emitPeers();
          }
        }
      } else if (topic.endsWith("/signal")) {
        if (data.to === this.selfId && data.from && data.payload) {
          this.callbacks.onSignal(data.from, data.payload);
        }
      }
    }
    broadcastPresence(type) {
      if (!this.client || !this.sessionCode) return;
      const topic = `ardel/v1/voice/${this.sessionCode}/presence`;
      const msg = new import_paho_mqtt.default.Message(
        JSON.stringify({
          type,
          from: this.selfId,
          peerId: this.selfId,
          name: this.displayName
        })
      );
      msg.destinationName = topic;
      msg.qos = 1;
      this.client.send(msg);
    }
    emitPeers() {
      const list = Array.from(this.members.entries()).map(([id, name]) => ({ id, name }));
      this.callbacks.onPeers(list);
    }
    sendSignal(toPeerId, payload) {
      if (this.useLocalFallback && this.localWs && this.localWs.readyState === WebSocket.OPEN) {
        this.localWs.send(
          JSON.stringify({
            type: "signal",
            to: toPeerId,
            payload
          })
        );
        return;
      }
      if (!this.client || !this.sessionCode) return;
      const topic = `ardel/v1/voice/${this.sessionCode}/signal`;
      const msg = new import_paho_mqtt.default.Message(
        JSON.stringify({
          type: "signal",
          from: this.selfId,
          to: toPeerId,
          payload
        })
      );
      msg.destinationName = topic;
      msg.qos = 1;
      this.client.send(msg);
    }
    connectLocalWs(wsUrl, create) {
      return new Promise((resolve, reject) => {
        this.useLocalFallback = true;
        const ws = new WebSocket(wsUrl);
        this.localWs = ws;
        ws.onopen = () => {
          ws.send(JSON.stringify({ type: "hello", displayName: this.displayName }));
          if (create) {
            ws.send(JSON.stringify({ type: "create" }));
          } else {
            ws.send(JSON.stringify({ type: "join", code: this.sessionCode }));
          }
          resolve();
        };
        ws.onerror = () => reject(new Error("Local signaling failed"));
        ws.onclose = () => this.callbacks.onDisconnect();
        ws.onmessage = (ev) => {
          let msg;
          try {
            msg = JSON.parse(String(ev.data));
          } catch {
            return;
          }
          switch (msg.type) {
            case "welcome":
              this.selfId = msg.peerId || this.selfId;
              break;
            case "created":
              this.sessionCode = msg.code || this.sessionCode;
              this.selfId = msg.peerId || this.selfId;
              this.callbacks.onCreated(this.sessionCode, this.selfId);
              break;
            case "joined":
              this.sessionCode = msg.code || this.sessionCode;
              this.selfId = msg.peerId || this.selfId;
              this.callbacks.onJoined(this.sessionCode, this.selfId);
              break;
            case "peers":
              this.callbacks.onPeers(msg.peers || []);
              break;
            case "signal":
              if (msg.from) this.callbacks.onSignal(msg.from, msg.payload);
              break;
            case "error":
              this.callbacks.onError(msg.message || "Generic error");
              break;
          }
        };
      });
    }
    leave() {
      if (this.client) {
        try {
          this.broadcastPresence("leave");
          this.client.disconnect();
        } catch {
        }
        this.client = null;
      }
      if (this.localWs) {
        try {
          this.localWs.send(JSON.stringify({ type: "leave" }));
          this.localWs.close();
        } catch {
        }
        this.localWs = null;
      }
      this.members.clear();
      this.sessionCode = null;
    }
  };

  // src/app.ts
  (() => {
    const boot = window.__ARDEL_VOICE__ || {};
    const params = new URLSearchParams(location.search);
    const wsUrl = boot.ws || params.get("ws") || "ws://127.0.0.1:17865/voice";
    const theme = boot.theme || params.get("theme") || "theme-dark";
    const iceServers = [
      { urls: "stun:stun.l.google.com:19302" },
      { urls: "stun:stun1.l.google.com:19302" },
      { urls: "stun:stun2.l.google.com:19302" },
      { urls: "stun:stun.cloudflare.com:3478" },
      { urls: "stun:stun.qq.com:3478" },
      { urls: "stun:stun.miwifi.com:3478" },
      { urls: "stun:stun.syncthing.net:3478" }
    ];
    let i18n = {};
    if (boot.i18n && typeof boot.i18n === "object") {
      i18n = boot.i18n;
    } else {
      try {
        i18n = JSON.parse(params.get("i18n") || "{}");
      } catch {
        i18n = {};
      }
    }
    function t(key, fallback) {
      const v = i18n[key];
      return typeof v === "string" && v.length ? v : fallback || key;
    }
    function applyI18n() {
      document.querySelectorAll("[data-i18n]").forEach((node) => {
        const key = node.getAttribute("data-i18n");
        if (key) node.textContent = t(key, node.textContent || "");
      });
      document.querySelectorAll("[data-i18n-placeholder]").forEach((node) => {
        const key = node.getAttribute("data-i18n-placeholder");
        if (key) {
          node.setAttribute(
            "placeholder",
            t(key, node.getAttribute("placeholder") || "")
          );
        }
      });
    }
    document.body.classList.remove("theme-dark", "theme-light");
    document.body.classList.add(theme === "theme-light" ? "theme-light" : "theme-dark");
    document.title = t("title", "Ardel Voice");
    applyI18n();
    const ui = {
      home: document.getElementById("stepHome"),
      join: document.getElementById("stepJoin"),
      live: document.getElementById("stepLive"),
      displayName: document.getElementById("displayName"),
      sessionCode: document.getElementById("sessionCode"),
      joinHint: document.getElementById("joinHint"),
      codeLabel: document.getElementById("codeLabel"),
      status: document.getElementById("status"),
      members: document.getElementById("members"),
      audioSink: document.getElementById("audioSink"),
      fieldHost: document.getElementById("fieldHost"),
      btnCreate: document.getElementById("btnCreate"),
      btnGoJoin: document.getElementById("btnGoJoin"),
      btnBackHome: document.getElementById("btnBackHome"),
      btnJoin: document.getElementById("btnJoin"),
      btnMute: document.getElementById("btnMute"),
      btnLeave: document.getElementById("btnLeave"),
      btnCopy: document.getElementById("btnCopy")
    };
    const bootName = typeof boot.name === "string" ? boot.name : "";
    ui.displayName.value = bootName || decodeURIComponent(params.get("name") || t("guest", "Guest"));
    const themeStyles = getComputedStyle(document.body);
    const field = window.VoiceField ? window.VoiceField.mount(ui.fieldHost, {
      ink: themeStyles.getPropertyValue("--field-ink").trim(),
      accent: themeStyles.getPropertyValue("--field-acc").trim()
    }) : null;
    let signaling = null;
    let selfId = null;
    let sessionCode = null;
    let localStream = null;
    let muted = false;
    let vad = null;
    let currentStep = "home";
    let stepBusy = false;
    let connectedTimer = 0;
    const stepOrder = { home: 0, join: 1, live: 2 };
    const pcs = /* @__PURE__ */ new Map();
    const names = /* @__PURE__ */ new Map();
    const connectedBanner = document.getElementById("connectedBanner");
    function displayName() {
      return ui.displayName.value.trim() || t("guest", "Guest");
    }
    function flashConnected() {
      if (!connectedBanner) return;
      connectedBanner.classList.remove("is-out");
      connectedBanner.classList.add("is-on");
      connectedBanner.setAttribute("aria-hidden", "false");
      window.clearTimeout(connectedTimer);
      connectedTimer = window.setTimeout(() => {
        connectedBanner.classList.remove("is-on");
        connectedBanner.classList.add("is-out");
        window.setTimeout(() => {
          connectedBanner.classList.remove("is-out");
          connectedBanner.setAttribute("aria-hidden", "true");
        }, 400);
      }, 2e3);
    }
    function stepEl(name) {
      if (name === "home") return ui.home;
      if (name === "join") return ui.join;
      return ui.live;
    }
    function notifyHostLayout(step) {
      try {
        const host = window.chrome?.webview;
        if (!host) return;
        const payload = { type: "layout", step };
        if (step === "join" || step === "live") {
          const panel = step === "live" ? ui.live : ui.join;
          if (step === "live") {
            const count = ui.members.querySelectorAll("li").length;
            payload.members = Math.max(1, count);
          }
          const card = panel.querySelector(".form-card");
          if (card) {
            const styles = getComputedStyle(panel);
            const padY = (parseFloat(styles.paddingTop) || 0) + (parseFloat(styles.paddingBottom) || 0);
            payload.height = Math.ceil(
              card.getBoundingClientRect().height + padY + 120
            );
          }
        }
        host.postMessage(payload);
      } catch {
      }
    }
    function scheduleHostLayout(step) {
      window.requestAnimationFrame(() => notifyHostLayout(step));
    }
    function showStep(name) {
      if (name === currentStep || stepBusy) {
        if (name === currentStep) {
          document.body.classList.toggle("view-home", name === "home");
          document.body.classList.toggle("view-form", name !== "home");
          field?.setLive(name === "live");
          scheduleHostLayout(name);
        }
        return;
      }
      const from = stepEl(currentStep);
      const to = stepEl(name);
      const forward = stepOrder[name] >= stepOrder[currentStep];
      stepBusy = true;
      from.classList.remove("is-active", "from-left", "from-right");
      from.classList.add("is-leaving", forward ? "to-left" : "to-right");
      from.setAttribute("aria-hidden", "true");
      to.classList.remove("is-leaving", "to-left", "to-right");
      to.classList.add(forward ? "from-right" : "from-left");
      void to.offsetWidth;
      to.classList.add("is-active");
      to.classList.remove("from-left", "from-right");
      to.setAttribute("aria-hidden", "false");
      currentStep = name;
      document.body.classList.toggle("view-home", name === "home");
      document.body.classList.toggle("view-form", name !== "home");
      field?.setLive(name === "live");
      scheduleHostLayout(name);
      window.setTimeout(() => {
        from.classList.remove("is-leaving", "to-left", "to-right");
        stepBusy = false;
        if (name === "join") ui.sessionCode.focus();
        if (name === "live") scheduleHostLayout("live");
      }, 320);
    }
    function setStatus(text) {
      ui.status.textContent = text;
    }
    function setJoinHint(text) {
      ui.joinHint.textContent = text || "";
    }
    function mapError(code) {
      if (code === "not_found") return t("errNotFound", "Session not found");
      if (code === "full") return t("errFull", "Session is full");
      return t("errGeneric", "Something went wrong");
    }
    function renderMembers() {
      ui.members.innerHTML = "";
      const rows = [
        { id: selfId, name: `${displayName()} ${t("youSuffix", "(you)")}` }
      ];
      for (const [id, name] of names) {
        if (id !== selfId) rows.push({ id, name });
      }
      for (const row of rows) {
        if (!row.id) continue;
        const li = document.createElement("li");
        li.textContent = row.name;
        ui.members.appendChild(li);
      }
      if (currentStep === "live") scheduleHostLayout("live");
    }
    async function ensureMic() {
      if (localStream) return localStream;
      localStream = await navigator.mediaDevices.getUserMedia({
        audio: micAudioConstraints(),
        video: false
      });
      for (const track of localStream.getAudioTracks()) {
        try {
          await track.applyConstraints(micAudioConstraints());
        } catch {
        }
      }
      vad?.stop();
      vad = startVadGate(localStream);
      vad.setUserMuted(muted);
      return localStream;
    }
    function stopMicPipeline() {
      vad?.stop();
      vad = null;
      if (localStream) {
        for (const track of localStream.getTracks()) track.stop();
        localStream = null;
      }
    }
    function closePeer(id) {
      const pc = pcs.get(id);
      if (!pc) return;
      try {
        pc.close();
      } catch {
      }
      pcs.delete(id);
      document.getElementById(`audio-${id}`)?.remove();
    }
    function closeAllPeers() {
      for (const id of [...pcs.keys()]) closePeer(id);
      names.clear();
    }
    async function createPeer(remoteId) {
      if (pcs.has(remoteId) || remoteId === selfId) return;
      const pc = new RTCPeerConnection({ iceServers });
      pcs.set(remoteId, pc);
      const stream = await ensureMic();
      for (const track of stream.getTracks()) pc.addTrack(track, stream);
      pc.onicecandidate = (ev) => {
        if (!ev.candidate || !signaling) return;
        signaling.sendSignal(remoteId, { kind: "ice", candidate: ev.candidate });
      };
      pc.ontrack = (ev) => {
        let audio = document.getElementById(`audio-${remoteId}`);
        if (!audio) {
          audio = document.createElement("audio");
          audio.id = `audio-${remoteId}`;
          audio.autoplay = true;
          audio.setAttribute("playsinline", "true");
          audio.volume = 1;
          ui.audioSink.appendChild(audio);
        }
        audio.srcObject = ev.streams[0] ?? null;
        void audio.play().catch(() => {
        });
      };
      pc.onconnectionstatechange = () => {
        if (pc.connectionState === "failed" || pc.connectionState === "closed") {
          closePeer(remoteId);
        }
      };
      if (selfId != null && selfId > remoteId) {
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        signaling?.sendSignal(remoteId, { kind: "sdp", description: pc.localDescription });
      }
    }
    async function syncPeers(peerList) {
      const remoteIds = /* @__PURE__ */ new Set();
      names.clear();
      for (const p of peerList) {
        names.set(p.id, p.name || t("guest", "Guest"));
        if (p.id !== selfId) remoteIds.add(p.id);
      }
      for (const id of [...pcs.keys()]) {
        if (!remoteIds.has(id)) closePeer(id);
      }
      for (const id of remoteIds) await createPeer(id);
      renderMembers();
    }
    async function onSignal(from, payload) {
      if (!payload || !from) return;
      let pc = pcs.get(from);
      if (!pc) {
        await createPeer(from);
        pc = pcs.get(from);
      }
      if (!pc) return;
      if (payload.kind === "sdp" && payload.description) {
        const desc = payload.description;
        await pc.setRemoteDescription(desc);
        if (desc.type === "offer") {
          const answer = await pc.createAnswer();
          await pc.setLocalDescription(answer);
          signaling?.sendSignal(from, { kind: "sdp", description: pc.localDescription });
        }
      } else if (payload.kind === "ice" && payload.candidate) {
        try {
          await pc.addIceCandidate(payload.candidate);
        } catch {
        }
      }
    }
    function initSignaling() {
      const chan = new DecentralizedSignalingChannel({
        onWelcome: (id) => {
          selfId = id;
        },
        onCreated: (code, id) => {
          sessionCode = code;
          selfId = id;
          ui.codeLabel.textContent = sessionCode || "";
          showStep("live");
          setStatus(t("statusInSession", "In session"));
        },
        onJoined: (code, id) => {
          sessionCode = code;
          selfId = id;
          ui.codeLabel.textContent = sessionCode || "";
          showStep("live");
          setStatus(t("statusInSession", "In session"));
          flashConnected();
        },
        onPeers: async (peers) => {
          await syncPeers(peers);
        },
        onSignal: async (from, payload) => {
          await onSignal(from, payload);
        },
        onError: (err) => {
          const msg = mapError(err);
          if (currentStep === "join") setJoinHint(msg);
          else setStatus(msg);
        },
        onDisconnect: () => {
          if (currentStep === "live") {
            setStatus(t("statusDisconnected", "Disconnected"));
          }
        }
      });
      chan.setDisplayName(displayName());
      return chan;
    }
    async function startSession(create) {
      if (create) scheduleHostLayout("live");
      try {
        setJoinHint("");
        setStatus(t("statusMic", "Requesting microphone\u2026"));
        await ensureMic();
        if (!signaling) {
          signaling = initSignaling();
        }
        signaling.setDisplayName(displayName());
        const code = create ? void 0 : (ui.sessionCode.value || "").trim().toUpperCase();
        if (!create && !code) {
          setJoinHint(t("statusNeedCode", "Enter an invite code"));
          return;
        }
        setStatus(t("statusSignaling", "Connecting\u2026"));
        await signaling.start(create, code, wsUrl);
      } catch (err) {
        const msg = err instanceof Error && err.message ? err.message : t("errGeneric", "Something went wrong");
        if (currentStep === "join") setJoinHint(msg);
        else setStatus(msg);
        if (create && currentStep === "home") scheduleHostLayout("home");
      }
    }
    function leave() {
      if (signaling) {
        signaling.leave();
        signaling = null;
      }
      closeAllPeers();
      stopMicPipeline();
      sessionCode = null;
      ui.members.innerHTML = "";
      muted = false;
      ui.btnMute.textContent = t("mute", "Mute mic");
      setJoinHint("");
      showStep("home");
    }
    ui.btnCreate.addEventListener("click", () => void startSession(true));
    ui.btnGoJoin.addEventListener("click", () => {
      setJoinHint("");
      showStep("join");
    });
    ui.btnBackHome.addEventListener("click", () => showStep("home"));
    ui.btnJoin.addEventListener("click", () => void startSession(false));
    ui.btnLeave.addEventListener("click", leave);
    ui.btnMute.addEventListener("click", () => {
      if (!localStream) return;
      muted = !muted;
      vad?.setUserMuted(muted);
      if (!vad) {
        for (const track of localStream.getAudioTracks()) track.enabled = !muted;
      }
      ui.btnMute.textContent = muted ? t("unmute", "Unmute mic") : t("mute", "Mute mic");
      void playMuteCue(muted);
    });
    ui.btnCopy.addEventListener("click", async () => {
      if (!sessionCode) return;
      try {
        await navigator.clipboard.writeText(sessionCode);
        setStatus(t("statusCopied", "Invite code copied"));
      } catch {
        setStatus(t("statusCopyFail", "Could not copy"));
      }
    });
    document.body.classList.add("view-home");
    document.body.classList.remove("view-form");
  })();
})();
