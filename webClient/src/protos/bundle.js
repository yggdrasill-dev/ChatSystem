/*eslint-disable block-scoped-var, id-length, no-control-regex, no-magic-numbers, no-prototype-builtins, no-redeclare, no-shadow, no-var, sort-vars*/
(function(global, factory) { /* global define, require, module */

    /* AMD */ if (typeof define === 'function' && define.amd)
        define(["protobufjs/minimal"], factory);

    /* CommonJS */ else if (typeof require === 'function' && typeof module === 'object' && module && module.exports)
        module.exports = factory(require("protobufjs/minimal"));

})(this, function($protobuf) {
    "use strict";

    // Common aliases
    var $Reader = $protobuf.Reader, $Writer = $protobuf.Writer, $util = $protobuf.util;
    
    // Exported root namespace
    var $root = $protobuf.roots["default"] || ($protobuf.roots["default"] = {});
    
    $root.chat = (function() {
    
        /**
         * Namespace chat.
         * @exports chat
         * @namespace
         */
        var chat = {};
    
        chat.Packet = (function() {
    
            /**
             * Properties of a Packet.
             * @memberof chat
             * @interface IPacket
             * @property {string|null} [subject] Packet subject
             * @property {Uint8Array|null} [payload] Packet payload
             */
    
            /**
             * Constructs a new Packet.
             * @memberof chat
             * @classdesc Represents a Packet.
             * @implements IPacket
             * @constructor
             * @param {chat.IPacket=} [properties] Properties to set
             */
            function Packet(properties) {
                if (properties)
                    for (var keys = Object.keys(properties), i = 0; i < keys.length; ++i)
                        if (properties[keys[i]] != null)
                            this[keys[i]] = properties[keys[i]];
            }
    
            /**
             * Packet subject.
             * @member {string} subject
             * @memberof chat.Packet
             * @instance
             */
            Packet.prototype.subject = "";
    
            /**
             * Packet payload.
             * @member {Uint8Array} payload
             * @memberof chat.Packet
             * @instance
             */
            Packet.prototype.payload = $util.newBuffer([]);
    
            /**
             * Creates a new Packet instance using the specified properties.
             * @function create
             * @memberof chat.Packet
             * @static
             * @param {chat.IPacket=} [properties] Properties to set
             * @returns {chat.Packet} Packet instance
             */
            Packet.create = function create(properties) {
                return new Packet(properties);
            };
    
            /**
             * Encodes the specified Packet message. Does not implicitly {@link chat.Packet.verify|verify} messages.
             * @function encode
             * @memberof chat.Packet
             * @static
             * @param {chat.IPacket} message Packet message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            Packet.encode = function encode(message, writer) {
                if (!writer)
                    writer = $Writer.create();
                if (message.subject != null && Object.hasOwnProperty.call(message, "subject"))
                    writer.uint32(/* id 1, wireType 2 =*/10).string(message.subject);
                if (message.payload != null && Object.hasOwnProperty.call(message, "payload"))
                    writer.uint32(/* id 2, wireType 2 =*/18).bytes(message.payload);
                return writer;
            };
    
            /**
             * Encodes the specified Packet message, length delimited. Does not implicitly {@link chat.Packet.verify|verify} messages.
             * @function encodeDelimited
             * @memberof chat.Packet
             * @static
             * @param {chat.IPacket} message Packet message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            Packet.encodeDelimited = function encodeDelimited(message, writer) {
                return this.encode(message, writer).ldelim();
            };
    
            /**
             * Decodes a Packet message from the specified reader or buffer.
             * @function decode
             * @memberof chat.Packet
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @param {number} [length] Message length if known beforehand
             * @returns {chat.Packet} Packet
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            Packet.decode = function decode(reader, length) {
                if (!(reader instanceof $Reader))
                    reader = $Reader.create(reader);
                var end = length === undefined ? reader.len : reader.pos + length, message = new $root.chat.Packet();
                while (reader.pos < end) {
                    var tag = reader.uint32();
                    switch (tag >>> 3) {
                    case 1:
                        message.subject = reader.string();
                        break;
                    case 2:
                        message.payload = reader.bytes();
                        break;
                    default:
                        reader.skipType(tag & 7);
                        break;
                    }
                }
                return message;
            };
    
            /**
             * Decodes a Packet message from the specified reader or buffer, length delimited.
             * @function decodeDelimited
             * @memberof chat.Packet
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @returns {chat.Packet} Packet
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            Packet.decodeDelimited = function decodeDelimited(reader) {
                if (!(reader instanceof $Reader))
                    reader = new $Reader(reader);
                return this.decode(reader, reader.uint32());
            };
    
            /**
             * Verifies a Packet message.
             * @function verify
             * @memberof chat.Packet
             * @static
             * @param {Object.<string,*>} message Plain object to verify
             * @returns {string|null} `null` if valid, otherwise the reason why it is not
             */
            Packet.verify = function verify(message) {
                if (typeof message !== "object" || message === null)
                    return "object expected";
                if (message.subject != null && message.hasOwnProperty("subject"))
                    if (!$util.isString(message.subject))
                        return "subject: string expected";
                if (message.payload != null && message.hasOwnProperty("payload"))
                    if (!(message.payload && typeof message.payload.length === "number" || $util.isString(message.payload)))
                        return "payload: buffer expected";
                return null;
            };
    
            /**
             * Creates a Packet message from a plain object. Also converts values to their respective internal types.
             * @function fromObject
             * @memberof chat.Packet
             * @static
             * @param {Object.<string,*>} object Plain object
             * @returns {chat.Packet} Packet
             */
            Packet.fromObject = function fromObject(object) {
                if (object instanceof $root.chat.Packet)
                    return object;
                var message = new $root.chat.Packet();
                if (object.subject != null)
                    message.subject = String(object.subject);
                if (object.payload != null)
                    if (typeof object.payload === "string")
                        $util.base64.decode(object.payload, message.payload = $util.newBuffer($util.base64.length(object.payload)), 0);
                    else if (object.payload.length)
                        message.payload = object.payload;
                return message;
            };
    
            /**
             * Creates a plain object from a Packet message. Also converts values to other types if specified.
             * @function toObject
             * @memberof chat.Packet
             * @static
             * @param {chat.Packet} message Packet
             * @param {$protobuf.IConversionOptions} [options] Conversion options
             * @returns {Object.<string,*>} Plain object
             */
            Packet.toObject = function toObject(message, options) {
                if (!options)
                    options = {};
                var object = {};
                if (options.defaults) {
                    object.subject = "";
                    if (options.bytes === String)
                        object.payload = "";
                    else {
                        object.payload = [];
                        if (options.bytes !== Array)
                            object.payload = $util.newBuffer(object.payload);
                    }
                }
                if (message.subject != null && message.hasOwnProperty("subject"))
                    object.subject = message.subject;
                if (message.payload != null && message.hasOwnProperty("payload"))
                    object.payload = options.bytes === String ? $util.base64.encode(message.payload, 0, message.payload.length) : options.bytes === Array ? Array.prototype.slice.call(message.payload) : message.payload;
                return object;
            };
    
            /**
             * Converts this Packet to JSON.
             * @function toJSON
             * @memberof chat.Packet
             * @instance
             * @returns {Object.<string,*>} JSON object
             */
            Packet.prototype.toJSON = function toJSON() {
                return this.constructor.toObject(this, $protobuf.util.toJSONOptions);
            };
    
            return Packet;
        })();
    
        /**
         * LoginStatus enum.
         * @name chat.LoginStatus
         * @enum {number}
         * @property {number} REJECT=0 REJECT value
         * @property {number} ACCPET=1 ACCPET value
         */
        chat.LoginStatus = (function() {
            var valuesById = {}, values = Object.create(valuesById);
            values[valuesById[0] = "REJECT"] = 0;
            values[valuesById[1] = "ACCPET"] = 1;
            return values;
        })();
    
        chat.LoginReply = (function() {
    
            /**
             * Properties of a LoginReply.
             * @memberof chat
             * @interface ILoginReply
             * @property {chat.LoginStatus|null} [status] LoginReply status
             * @property {string|null} [name] LoginReply name
             * @property {string|null} [room] LoginReply room
             */
    
            /**
             * Constructs a new LoginReply.
             * @memberof chat
             * @classdesc Represents a LoginReply.
             * @implements ILoginReply
             * @constructor
             * @param {chat.ILoginReply=} [properties] Properties to set
             */
            function LoginReply(properties) {
                if (properties)
                    for (var keys = Object.keys(properties), i = 0; i < keys.length; ++i)
                        if (properties[keys[i]] != null)
                            this[keys[i]] = properties[keys[i]];
            }
    
            /**
             * LoginReply status.
             * @member {chat.LoginStatus} status
             * @memberof chat.LoginReply
             * @instance
             */
            LoginReply.prototype.status = 0;
    
            /**
             * LoginReply name.
             * @member {string} name
             * @memberof chat.LoginReply
             * @instance
             */
            LoginReply.prototype.name = "";
    
            /**
             * LoginReply room.
             * @member {string} room
             * @memberof chat.LoginReply
             * @instance
             */
            LoginReply.prototype.room = "";
    
            /**
             * Creates a new LoginReply instance using the specified properties.
             * @function create
             * @memberof chat.LoginReply
             * @static
             * @param {chat.ILoginReply=} [properties] Properties to set
             * @returns {chat.LoginReply} LoginReply instance
             */
            LoginReply.create = function create(properties) {
                return new LoginReply(properties);
            };
    
            /**
             * Encodes the specified LoginReply message. Does not implicitly {@link chat.LoginReply.verify|verify} messages.
             * @function encode
             * @memberof chat.LoginReply
             * @static
             * @param {chat.ILoginReply} message LoginReply message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            LoginReply.encode = function encode(message, writer) {
                if (!writer)
                    writer = $Writer.create();
                if (message.status != null && Object.hasOwnProperty.call(message, "status"))
                    writer.uint32(/* id 1, wireType 0 =*/8).int32(message.status);
                if (message.name != null && Object.hasOwnProperty.call(message, "name"))
                    writer.uint32(/* id 2, wireType 2 =*/18).string(message.name);
                if (message.room != null && Object.hasOwnProperty.call(message, "room"))
                    writer.uint32(/* id 3, wireType 2 =*/26).string(message.room);
                return writer;
            };
    
            /**
             * Encodes the specified LoginReply message, length delimited. Does not implicitly {@link chat.LoginReply.verify|verify} messages.
             * @function encodeDelimited
             * @memberof chat.LoginReply
             * @static
             * @param {chat.ILoginReply} message LoginReply message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            LoginReply.encodeDelimited = function encodeDelimited(message, writer) {
                return this.encode(message, writer).ldelim();
            };
    
            /**
             * Decodes a LoginReply message from the specified reader or buffer.
             * @function decode
             * @memberof chat.LoginReply
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @param {number} [length] Message length if known beforehand
             * @returns {chat.LoginReply} LoginReply
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            LoginReply.decode = function decode(reader, length) {
                if (!(reader instanceof $Reader))
                    reader = $Reader.create(reader);
                var end = length === undefined ? reader.len : reader.pos + length, message = new $root.chat.LoginReply();
                while (reader.pos < end) {
                    var tag = reader.uint32();
                    switch (tag >>> 3) {
                    case 1:
                        message.status = reader.int32();
                        break;
                    case 2:
                        message.name = reader.string();
                        break;
                    case 3:
                        message.room = reader.string();
                        break;
                    default:
                        reader.skipType(tag & 7);
                        break;
                    }
                }
                return message;
            };
    
            /**
             * Decodes a LoginReply message from the specified reader or buffer, length delimited.
             * @function decodeDelimited
             * @memberof chat.LoginReply
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @returns {chat.LoginReply} LoginReply
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            LoginReply.decodeDelimited = function decodeDelimited(reader) {
                if (!(reader instanceof $Reader))
                    reader = new $Reader(reader);
                return this.decode(reader, reader.uint32());
            };
    
            /**
             * Verifies a LoginReply message.
             * @function verify
             * @memberof chat.LoginReply
             * @static
             * @param {Object.<string,*>} message Plain object to verify
             * @returns {string|null} `null` if valid, otherwise the reason why it is not
             */
            LoginReply.verify = function verify(message) {
                if (typeof message !== "object" || message === null)
                    return "object expected";
                if (message.status != null && message.hasOwnProperty("status"))
                    switch (message.status) {
                    default:
                        return "status: enum value expected";
                    case 0:
                    case 1:
                        break;
                    }
                if (message.name != null && message.hasOwnProperty("name"))
                    if (!$util.isString(message.name))
                        return "name: string expected";
                if (message.room != null && message.hasOwnProperty("room"))
                    if (!$util.isString(message.room))
                        return "room: string expected";
                return null;
            };
    
            /**
             * Creates a LoginReply message from a plain object. Also converts values to their respective internal types.
             * @function fromObject
             * @memberof chat.LoginReply
             * @static
             * @param {Object.<string,*>} object Plain object
             * @returns {chat.LoginReply} LoginReply
             */
            LoginReply.fromObject = function fromObject(object) {
                if (object instanceof $root.chat.LoginReply)
                    return object;
                var message = new $root.chat.LoginReply();
                switch (object.status) {
                case "REJECT":
                case 0:
                    message.status = 0;
                    break;
                case "ACCPET":
                case 1:
                    message.status = 1;
                    break;
                }
                if (object.name != null)
                    message.name = String(object.name);
                if (object.room != null)
                    message.room = String(object.room);
                return message;
            };
    
            /**
             * Creates a plain object from a LoginReply message. Also converts values to other types if specified.
             * @function toObject
             * @memberof chat.LoginReply
             * @static
             * @param {chat.LoginReply} message LoginReply
             * @param {$protobuf.IConversionOptions} [options] Conversion options
             * @returns {Object.<string,*>} Plain object
             */
            LoginReply.toObject = function toObject(message, options) {
                if (!options)
                    options = {};
                var object = {};
                if (options.defaults) {
                    object.status = options.enums === String ? "REJECT" : 0;
                    object.name = "";
                    object.room = "";
                }
                if (message.status != null && message.hasOwnProperty("status"))
                    object.status = options.enums === String ? $root.chat.LoginStatus[message.status] : message.status;
                if (message.name != null && message.hasOwnProperty("name"))
                    object.name = message.name;
                if (message.room != null && message.hasOwnProperty("room"))
                    object.room = message.room;
                return object;
            };
    
            /**
             * Converts this LoginReply to JSON.
             * @function toJSON
             * @memberof chat.LoginReply
             * @instance
             * @returns {Object.<string,*>} JSON object
             */
            LoginReply.prototype.toJSON = function toJSON() {
                return this.constructor.toObject(this, $protobuf.util.toJSONOptions);
            };
    
            return LoginReply;
        })();
    
        /**
         * Scope enum.
         * @name chat.Scope
         * @enum {number}
         * @property {number} NONE=0 NONE value
         * @property {number} PERSON=1 PERSON value
         * @property {number} ROOM=2 ROOM value
         * @property {number} SYSTEM=3 SYSTEM value
         */
        chat.Scope = (function() {
            var valuesById = {}, values = Object.create(valuesById);
            values[valuesById[0] = "NONE"] = 0;
            values[valuesById[1] = "PERSON"] = 1;
            values[valuesById[2] = "ROOM"] = 2;
            values[valuesById[3] = "SYSTEM"] = 3;
            return values;
        })();
    
        chat.ChatMessage = (function() {
    
            /**
             * Properties of a ChatMessage.
             * @memberof chat
             * @interface IChatMessage
             * @property {chat.Scope|null} [scope] ChatMessage scope
             * @property {string|null} [message] ChatMessage message
             * @property {string|null} [target] ChatMessage target
             * @property {string|null} [from] ChatMessage from
             */
    
            /**
             * Constructs a new ChatMessage.
             * @memberof chat
             * @classdesc Represents a ChatMessage.
             * @implements IChatMessage
             * @constructor
             * @param {chat.IChatMessage=} [properties] Properties to set
             */
            function ChatMessage(properties) {
                if (properties)
                    for (var keys = Object.keys(properties), i = 0; i < keys.length; ++i)
                        if (properties[keys[i]] != null)
                            this[keys[i]] = properties[keys[i]];
            }
    
            /**
             * ChatMessage scope.
             * @member {chat.Scope} scope
             * @memberof chat.ChatMessage
             * @instance
             */
            ChatMessage.prototype.scope = 0;
    
            /**
             * ChatMessage message.
             * @member {string} message
             * @memberof chat.ChatMessage
             * @instance
             */
            ChatMessage.prototype.message = "";
    
            /**
             * ChatMessage target.
             * @member {string} target
             * @memberof chat.ChatMessage
             * @instance
             */
            ChatMessage.prototype.target = "";
    
            /**
             * ChatMessage from.
             * @member {string} from
             * @memberof chat.ChatMessage
             * @instance
             */
            ChatMessage.prototype.from = "";
    
            /**
             * Creates a new ChatMessage instance using the specified properties.
             * @function create
             * @memberof chat.ChatMessage
             * @static
             * @param {chat.IChatMessage=} [properties] Properties to set
             * @returns {chat.ChatMessage} ChatMessage instance
             */
            ChatMessage.create = function create(properties) {
                return new ChatMessage(properties);
            };
    
            /**
             * Encodes the specified ChatMessage message. Does not implicitly {@link chat.ChatMessage.verify|verify} messages.
             * @function encode
             * @memberof chat.ChatMessage
             * @static
             * @param {chat.IChatMessage} message ChatMessage message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            ChatMessage.encode = function encode(message, writer) {
                if (!writer)
                    writer = $Writer.create();
                if (message.scope != null && Object.hasOwnProperty.call(message, "scope"))
                    writer.uint32(/* id 1, wireType 0 =*/8).int32(message.scope);
                if (message.message != null && Object.hasOwnProperty.call(message, "message"))
                    writer.uint32(/* id 2, wireType 2 =*/18).string(message.message);
                if (message.target != null && Object.hasOwnProperty.call(message, "target"))
                    writer.uint32(/* id 3, wireType 2 =*/26).string(message.target);
                if (message.from != null && Object.hasOwnProperty.call(message, "from"))
                    writer.uint32(/* id 4, wireType 2 =*/34).string(message.from);
                return writer;
            };
    
            /**
             * Encodes the specified ChatMessage message, length delimited. Does not implicitly {@link chat.ChatMessage.verify|verify} messages.
             * @function encodeDelimited
             * @memberof chat.ChatMessage
             * @static
             * @param {chat.IChatMessage} message ChatMessage message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            ChatMessage.encodeDelimited = function encodeDelimited(message, writer) {
                return this.encode(message, writer).ldelim();
            };
    
            /**
             * Decodes a ChatMessage message from the specified reader or buffer.
             * @function decode
             * @memberof chat.ChatMessage
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @param {number} [length] Message length if known beforehand
             * @returns {chat.ChatMessage} ChatMessage
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            ChatMessage.decode = function decode(reader, length) {
                if (!(reader instanceof $Reader))
                    reader = $Reader.create(reader);
                var end = length === undefined ? reader.len : reader.pos + length, message = new $root.chat.ChatMessage();
                while (reader.pos < end) {
                    var tag = reader.uint32();
                    switch (tag >>> 3) {
                    case 1:
                        message.scope = reader.int32();
                        break;
                    case 2:
                        message.message = reader.string();
                        break;
                    case 3:
                        message.target = reader.string();
                        break;
                    case 4:
                        message.from = reader.string();
                        break;
                    default:
                        reader.skipType(tag & 7);
                        break;
                    }
                }
                return message;
            };
    
            /**
             * Decodes a ChatMessage message from the specified reader or buffer, length delimited.
             * @function decodeDelimited
             * @memberof chat.ChatMessage
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @returns {chat.ChatMessage} ChatMessage
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            ChatMessage.decodeDelimited = function decodeDelimited(reader) {
                if (!(reader instanceof $Reader))
                    reader = new $Reader(reader);
                return this.decode(reader, reader.uint32());
            };
    
            /**
             * Verifies a ChatMessage message.
             * @function verify
             * @memberof chat.ChatMessage
             * @static
             * @param {Object.<string,*>} message Plain object to verify
             * @returns {string|null} `null` if valid, otherwise the reason why it is not
             */
            ChatMessage.verify = function verify(message) {
                if (typeof message !== "object" || message === null)
                    return "object expected";
                if (message.scope != null && message.hasOwnProperty("scope"))
                    switch (message.scope) {
                    default:
                        return "scope: enum value expected";
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                        break;
                    }
                if (message.message != null && message.hasOwnProperty("message"))
                    if (!$util.isString(message.message))
                        return "message: string expected";
                if (message.target != null && message.hasOwnProperty("target"))
                    if (!$util.isString(message.target))
                        return "target: string expected";
                if (message.from != null && message.hasOwnProperty("from"))
                    if (!$util.isString(message.from))
                        return "from: string expected";
                return null;
            };
    
            /**
             * Creates a ChatMessage message from a plain object. Also converts values to their respective internal types.
             * @function fromObject
             * @memberof chat.ChatMessage
             * @static
             * @param {Object.<string,*>} object Plain object
             * @returns {chat.ChatMessage} ChatMessage
             */
            ChatMessage.fromObject = function fromObject(object) {
                if (object instanceof $root.chat.ChatMessage)
                    return object;
                var message = new $root.chat.ChatMessage();
                switch (object.scope) {
                case "NONE":
                case 0:
                    message.scope = 0;
                    break;
                case "PERSON":
                case 1:
                    message.scope = 1;
                    break;
                case "ROOM":
                case 2:
                    message.scope = 2;
                    break;
                case "SYSTEM":
                case 3:
                    message.scope = 3;
                    break;
                }
                if (object.message != null)
                    message.message = String(object.message);
                if (object.target != null)
                    message.target = String(object.target);
                if (object.from != null)
                    message.from = String(object.from);
                return message;
            };
    
            /**
             * Creates a plain object from a ChatMessage message. Also converts values to other types if specified.
             * @function toObject
             * @memberof chat.ChatMessage
             * @static
             * @param {chat.ChatMessage} message ChatMessage
             * @param {$protobuf.IConversionOptions} [options] Conversion options
             * @returns {Object.<string,*>} Plain object
             */
            ChatMessage.toObject = function toObject(message, options) {
                if (!options)
                    options = {};
                var object = {};
                if (options.defaults) {
                    object.scope = options.enums === String ? "NONE" : 0;
                    object.message = "";
                    object.target = "";
                    object.from = "";
                }
                if (message.scope != null && message.hasOwnProperty("scope"))
                    object.scope = options.enums === String ? $root.chat.Scope[message.scope] : message.scope;
                if (message.message != null && message.hasOwnProperty("message"))
                    object.message = message.message;
                if (message.target != null && message.hasOwnProperty("target"))
                    object.target = message.target;
                if (message.from != null && message.hasOwnProperty("from"))
                    object.from = message.from;
                return object;
            };
    
            /**
             * Converts this ChatMessage to JSON.
             * @function toJSON
             * @memberof chat.ChatMessage
             * @instance
             * @returns {Object.<string,*>} JSON object
             */
            ChatMessage.prototype.toJSON = function toJSON() {
                return this.constructor.toObject(this, $protobuf.util.toJSONOptions);
            };
    
            return ChatMessage;
        })();
    
        chat.PlayerList = (function() {
    
            /**
             * Properties of a PlayerList.
             * @memberof chat
             * @interface IPlayerList
             * @property {Array.<string>|null} [players] PlayerList players
             */
    
            /**
             * Constructs a new PlayerList.
             * @memberof chat
             * @classdesc Represents a PlayerList.
             * @implements IPlayerList
             * @constructor
             * @param {chat.IPlayerList=} [properties] Properties to set
             */
            function PlayerList(properties) {
                this.players = [];
                if (properties)
                    for (var keys = Object.keys(properties), i = 0; i < keys.length; ++i)
                        if (properties[keys[i]] != null)
                            this[keys[i]] = properties[keys[i]];
            }
    
            /**
             * PlayerList players.
             * @member {Array.<string>} players
             * @memberof chat.PlayerList
             * @instance
             */
            PlayerList.prototype.players = $util.emptyArray;
    
            /**
             * Creates a new PlayerList instance using the specified properties.
             * @function create
             * @memberof chat.PlayerList
             * @static
             * @param {chat.IPlayerList=} [properties] Properties to set
             * @returns {chat.PlayerList} PlayerList instance
             */
            PlayerList.create = function create(properties) {
                return new PlayerList(properties);
            };
    
            /**
             * Encodes the specified PlayerList message. Does not implicitly {@link chat.PlayerList.verify|verify} messages.
             * @function encode
             * @memberof chat.PlayerList
             * @static
             * @param {chat.IPlayerList} message PlayerList message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            PlayerList.encode = function encode(message, writer) {
                if (!writer)
                    writer = $Writer.create();
                if (message.players != null && message.players.length)
                    for (var i = 0; i < message.players.length; ++i)
                        writer.uint32(/* id 1, wireType 2 =*/10).string(message.players[i]);
                return writer;
            };
    
            /**
             * Encodes the specified PlayerList message, length delimited. Does not implicitly {@link chat.PlayerList.verify|verify} messages.
             * @function encodeDelimited
             * @memberof chat.PlayerList
             * @static
             * @param {chat.IPlayerList} message PlayerList message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            PlayerList.encodeDelimited = function encodeDelimited(message, writer) {
                return this.encode(message, writer).ldelim();
            };
    
            /**
             * Decodes a PlayerList message from the specified reader or buffer.
             * @function decode
             * @memberof chat.PlayerList
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @param {number} [length] Message length if known beforehand
             * @returns {chat.PlayerList} PlayerList
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            PlayerList.decode = function decode(reader, length) {
                if (!(reader instanceof $Reader))
                    reader = $Reader.create(reader);
                var end = length === undefined ? reader.len : reader.pos + length, message = new $root.chat.PlayerList();
                while (reader.pos < end) {
                    var tag = reader.uint32();
                    switch (tag >>> 3) {
                    case 1:
                        if (!(message.players && message.players.length))
                            message.players = [];
                        message.players.push(reader.string());
                        break;
                    default:
                        reader.skipType(tag & 7);
                        break;
                    }
                }
                return message;
            };
    
            /**
             * Decodes a PlayerList message from the specified reader or buffer, length delimited.
             * @function decodeDelimited
             * @memberof chat.PlayerList
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @returns {chat.PlayerList} PlayerList
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            PlayerList.decodeDelimited = function decodeDelimited(reader) {
                if (!(reader instanceof $Reader))
                    reader = new $Reader(reader);
                return this.decode(reader, reader.uint32());
            };
    
            /**
             * Verifies a PlayerList message.
             * @function verify
             * @memberof chat.PlayerList
             * @static
             * @param {Object.<string,*>} message Plain object to verify
             * @returns {string|null} `null` if valid, otherwise the reason why it is not
             */
            PlayerList.verify = function verify(message) {
                if (typeof message !== "object" || message === null)
                    return "object expected";
                if (message.players != null && message.hasOwnProperty("players")) {
                    if (!Array.isArray(message.players))
                        return "players: array expected";
                    for (var i = 0; i < message.players.length; ++i)
                        if (!$util.isString(message.players[i]))
                            return "players: string[] expected";
                }
                return null;
            };
    
            /**
             * Creates a PlayerList message from a plain object. Also converts values to their respective internal types.
             * @function fromObject
             * @memberof chat.PlayerList
             * @static
             * @param {Object.<string,*>} object Plain object
             * @returns {chat.PlayerList} PlayerList
             */
            PlayerList.fromObject = function fromObject(object) {
                if (object instanceof $root.chat.PlayerList)
                    return object;
                var message = new $root.chat.PlayerList();
                if (object.players) {
                    if (!Array.isArray(object.players))
                        throw TypeError(".chat.PlayerList.players: array expected");
                    message.players = [];
                    for (var i = 0; i < object.players.length; ++i)
                        message.players[i] = String(object.players[i]);
                }
                return message;
            };
    
            /**
             * Creates a plain object from a PlayerList message. Also converts values to other types if specified.
             * @function toObject
             * @memberof chat.PlayerList
             * @static
             * @param {chat.PlayerList} message PlayerList
             * @param {$protobuf.IConversionOptions} [options] Conversion options
             * @returns {Object.<string,*>} Plain object
             */
            PlayerList.toObject = function toObject(message, options) {
                if (!options)
                    options = {};
                var object = {};
                if (options.arrays || options.defaults)
                    object.players = [];
                if (message.players && message.players.length) {
                    object.players = [];
                    for (var j = 0; j < message.players.length; ++j)
                        object.players[j] = message.players[j];
                }
                return object;
            };
    
            /**
             * Converts this PlayerList to JSON.
             * @function toJSON
             * @memberof chat.PlayerList
             * @instance
             * @returns {Object.<string,*>} JSON object
             */
            PlayerList.prototype.toJSON = function toJSON() {
                return this.constructor.toObject(this, $protobuf.util.toJSONOptions);
            };
    
            return PlayerList;
        })();
    
        chat.RoomList = (function() {
    
            /**
             * Properties of a RoomList.
             * @memberof chat
             * @interface IRoomList
             * @property {Array.<string>|null} [rooms] RoomList rooms
             */
    
            /**
             * Constructs a new RoomList.
             * @memberof chat
             * @classdesc Represents a RoomList.
             * @implements IRoomList
             * @constructor
             * @param {chat.IRoomList=} [properties] Properties to set
             */
            function RoomList(properties) {
                this.rooms = [];
                if (properties)
                    for (var keys = Object.keys(properties), i = 0; i < keys.length; ++i)
                        if (properties[keys[i]] != null)
                            this[keys[i]] = properties[keys[i]];
            }
    
            /**
             * RoomList rooms.
             * @member {Array.<string>} rooms
             * @memberof chat.RoomList
             * @instance
             */
            RoomList.prototype.rooms = $util.emptyArray;
    
            /**
             * Creates a new RoomList instance using the specified properties.
             * @function create
             * @memberof chat.RoomList
             * @static
             * @param {chat.IRoomList=} [properties] Properties to set
             * @returns {chat.RoomList} RoomList instance
             */
            RoomList.create = function create(properties) {
                return new RoomList(properties);
            };
    
            /**
             * Encodes the specified RoomList message. Does not implicitly {@link chat.RoomList.verify|verify} messages.
             * @function encode
             * @memberof chat.RoomList
             * @static
             * @param {chat.IRoomList} message RoomList message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            RoomList.encode = function encode(message, writer) {
                if (!writer)
                    writer = $Writer.create();
                if (message.rooms != null && message.rooms.length)
                    for (var i = 0; i < message.rooms.length; ++i)
                        writer.uint32(/* id 1, wireType 2 =*/10).string(message.rooms[i]);
                return writer;
            };
    
            /**
             * Encodes the specified RoomList message, length delimited. Does not implicitly {@link chat.RoomList.verify|verify} messages.
             * @function encodeDelimited
             * @memberof chat.RoomList
             * @static
             * @param {chat.IRoomList} message RoomList message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            RoomList.encodeDelimited = function encodeDelimited(message, writer) {
                return this.encode(message, writer).ldelim();
            };
    
            /**
             * Decodes a RoomList message from the specified reader or buffer.
             * @function decode
             * @memberof chat.RoomList
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @param {number} [length] Message length if known beforehand
             * @returns {chat.RoomList} RoomList
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            RoomList.decode = function decode(reader, length) {
                if (!(reader instanceof $Reader))
                    reader = $Reader.create(reader);
                var end = length === undefined ? reader.len : reader.pos + length, message = new $root.chat.RoomList();
                while (reader.pos < end) {
                    var tag = reader.uint32();
                    switch (tag >>> 3) {
                    case 1:
                        if (!(message.rooms && message.rooms.length))
                            message.rooms = [];
                        message.rooms.push(reader.string());
                        break;
                    default:
                        reader.skipType(tag & 7);
                        break;
                    }
                }
                return message;
            };
    
            /**
             * Decodes a RoomList message from the specified reader or buffer, length delimited.
             * @function decodeDelimited
             * @memberof chat.RoomList
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @returns {chat.RoomList} RoomList
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            RoomList.decodeDelimited = function decodeDelimited(reader) {
                if (!(reader instanceof $Reader))
                    reader = new $Reader(reader);
                return this.decode(reader, reader.uint32());
            };
    
            /**
             * Verifies a RoomList message.
             * @function verify
             * @memberof chat.RoomList
             * @static
             * @param {Object.<string,*>} message Plain object to verify
             * @returns {string|null} `null` if valid, otherwise the reason why it is not
             */
            RoomList.verify = function verify(message) {
                if (typeof message !== "object" || message === null)
                    return "object expected";
                if (message.rooms != null && message.hasOwnProperty("rooms")) {
                    if (!Array.isArray(message.rooms))
                        return "rooms: array expected";
                    for (var i = 0; i < message.rooms.length; ++i)
                        if (!$util.isString(message.rooms[i]))
                            return "rooms: string[] expected";
                }
                return null;
            };
    
            /**
             * Creates a RoomList message from a plain object. Also converts values to their respective internal types.
             * @function fromObject
             * @memberof chat.RoomList
             * @static
             * @param {Object.<string,*>} object Plain object
             * @returns {chat.RoomList} RoomList
             */
            RoomList.fromObject = function fromObject(object) {
                if (object instanceof $root.chat.RoomList)
                    return object;
                var message = new $root.chat.RoomList();
                if (object.rooms) {
                    if (!Array.isArray(object.rooms))
                        throw TypeError(".chat.RoomList.rooms: array expected");
                    message.rooms = [];
                    for (var i = 0; i < object.rooms.length; ++i)
                        message.rooms[i] = String(object.rooms[i]);
                }
                return message;
            };
    
            /**
             * Creates a plain object from a RoomList message. Also converts values to other types if specified.
             * @function toObject
             * @memberof chat.RoomList
             * @static
             * @param {chat.RoomList} message RoomList
             * @param {$protobuf.IConversionOptions} [options] Conversion options
             * @returns {Object.<string,*>} Plain object
             */
            RoomList.toObject = function toObject(message, options) {
                if (!options)
                    options = {};
                var object = {};
                if (options.arrays || options.defaults)
                    object.rooms = [];
                if (message.rooms && message.rooms.length) {
                    object.rooms = [];
                    for (var j = 0; j < message.rooms.length; ++j)
                        object.rooms[j] = message.rooms[j];
                }
                return object;
            };
    
            /**
             * Converts this RoomList to JSON.
             * @function toJSON
             * @memberof chat.RoomList
             * @instance
             * @returns {Object.<string,*>} JSON object
             */
            RoomList.prototype.toJSON = function toJSON() {
                return this.constructor.toObject(this, $protobuf.util.toJSONOptions);
            };
    
            return RoomList;
        })();
    
        return chat;
    })();

    return $root;
});
