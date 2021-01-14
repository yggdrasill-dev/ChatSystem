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
    
        chat.ChatMessage = (function() {
    
            /**
             * Properties of a ChatMessage.
             * @memberof chat
             * @interface IChatMessage
             * @property {string|null} [subject] ChatMessage subject
             * @property {Uint8Array|null} [payload] ChatMessage payload
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
             * ChatMessage subject.
             * @member {string} subject
             * @memberof chat.ChatMessage
             * @instance
             */
            ChatMessage.prototype.subject = "";
    
            /**
             * ChatMessage payload.
             * @member {Uint8Array} payload
             * @memberof chat.ChatMessage
             * @instance
             */
            ChatMessage.prototype.payload = $util.newBuffer([]);
    
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
                if (message.subject != null && Object.hasOwnProperty.call(message, "subject"))
                    writer.uint32(/* id 1, wireType 2 =*/10).string(message.subject);
                if (message.payload != null && Object.hasOwnProperty.call(message, "payload"))
                    writer.uint32(/* id 2, wireType 2 =*/18).bytes(message.payload);
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
                if (message.subject != null && message.hasOwnProperty("subject"))
                    if (!$util.isString(message.subject))
                        return "subject: string expected";
                if (message.payload != null && message.hasOwnProperty("payload"))
                    if (!(message.payload && typeof message.payload.length === "number" || $util.isString(message.payload)))
                        return "payload: buffer expected";
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
    
        chat.LoginRegistration = (function() {
    
            /**
             * Properties of a LoginRegistration.
             * @memberof chat
             * @interface ILoginRegistration
             * @property {string|null} [name] LoginRegistration name
             * @property {string|null} [channel] LoginRegistration channel
             */
    
            /**
             * Constructs a new LoginRegistration.
             * @memberof chat
             * @classdesc Represents a LoginRegistration.
             * @implements ILoginRegistration
             * @constructor
             * @param {chat.ILoginRegistration=} [properties] Properties to set
             */
            function LoginRegistration(properties) {
                if (properties)
                    for (var keys = Object.keys(properties), i = 0; i < keys.length; ++i)
                        if (properties[keys[i]] != null)
                            this[keys[i]] = properties[keys[i]];
            }
    
            /**
             * LoginRegistration name.
             * @member {string} name
             * @memberof chat.LoginRegistration
             * @instance
             */
            LoginRegistration.prototype.name = "";
    
            /**
             * LoginRegistration channel.
             * @member {string} channel
             * @memberof chat.LoginRegistration
             * @instance
             */
            LoginRegistration.prototype.channel = "";
    
            /**
             * Creates a new LoginRegistration instance using the specified properties.
             * @function create
             * @memberof chat.LoginRegistration
             * @static
             * @param {chat.ILoginRegistration=} [properties] Properties to set
             * @returns {chat.LoginRegistration} LoginRegistration instance
             */
            LoginRegistration.create = function create(properties) {
                return new LoginRegistration(properties);
            };
    
            /**
             * Encodes the specified LoginRegistration message. Does not implicitly {@link chat.LoginRegistration.verify|verify} messages.
             * @function encode
             * @memberof chat.LoginRegistration
             * @static
             * @param {chat.ILoginRegistration} message LoginRegistration message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            LoginRegistration.encode = function encode(message, writer) {
                if (!writer)
                    writer = $Writer.create();
                if (message.name != null && Object.hasOwnProperty.call(message, "name"))
                    writer.uint32(/* id 1, wireType 2 =*/10).string(message.name);
                if (message.channel != null && Object.hasOwnProperty.call(message, "channel"))
                    writer.uint32(/* id 2, wireType 2 =*/18).string(message.channel);
                return writer;
            };
    
            /**
             * Encodes the specified LoginRegistration message, length delimited. Does not implicitly {@link chat.LoginRegistration.verify|verify} messages.
             * @function encodeDelimited
             * @memberof chat.LoginRegistration
             * @static
             * @param {chat.ILoginRegistration} message LoginRegistration message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            LoginRegistration.encodeDelimited = function encodeDelimited(message, writer) {
                return this.encode(message, writer).ldelim();
            };
    
            /**
             * Decodes a LoginRegistration message from the specified reader or buffer.
             * @function decode
             * @memberof chat.LoginRegistration
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @param {number} [length] Message length if known beforehand
             * @returns {chat.LoginRegistration} LoginRegistration
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            LoginRegistration.decode = function decode(reader, length) {
                if (!(reader instanceof $Reader))
                    reader = $Reader.create(reader);
                var end = length === undefined ? reader.len : reader.pos + length, message = new $root.chat.LoginRegistration();
                while (reader.pos < end) {
                    var tag = reader.uint32();
                    switch (tag >>> 3) {
                    case 1:
                        message.name = reader.string();
                        break;
                    case 2:
                        message.channel = reader.string();
                        break;
                    default:
                        reader.skipType(tag & 7);
                        break;
                    }
                }
                return message;
            };
    
            /**
             * Decodes a LoginRegistration message from the specified reader or buffer, length delimited.
             * @function decodeDelimited
             * @memberof chat.LoginRegistration
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @returns {chat.LoginRegistration} LoginRegistration
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            LoginRegistration.decodeDelimited = function decodeDelimited(reader) {
                if (!(reader instanceof $Reader))
                    reader = new $Reader(reader);
                return this.decode(reader, reader.uint32());
            };
    
            /**
             * Verifies a LoginRegistration message.
             * @function verify
             * @memberof chat.LoginRegistration
             * @static
             * @param {Object.<string,*>} message Plain object to verify
             * @returns {string|null} `null` if valid, otherwise the reason why it is not
             */
            LoginRegistration.verify = function verify(message) {
                if (typeof message !== "object" || message === null)
                    return "object expected";
                if (message.name != null && message.hasOwnProperty("name"))
                    if (!$util.isString(message.name))
                        return "name: string expected";
                if (message.channel != null && message.hasOwnProperty("channel"))
                    if (!$util.isString(message.channel))
                        return "channel: string expected";
                return null;
            };
    
            /**
             * Creates a LoginRegistration message from a plain object. Also converts values to their respective internal types.
             * @function fromObject
             * @memberof chat.LoginRegistration
             * @static
             * @param {Object.<string,*>} object Plain object
             * @returns {chat.LoginRegistration} LoginRegistration
             */
            LoginRegistration.fromObject = function fromObject(object) {
                if (object instanceof $root.chat.LoginRegistration)
                    return object;
                var message = new $root.chat.LoginRegistration();
                if (object.name != null)
                    message.name = String(object.name);
                if (object.channel != null)
                    message.channel = String(object.channel);
                return message;
            };
    
            /**
             * Creates a plain object from a LoginRegistration message. Also converts values to other types if specified.
             * @function toObject
             * @memberof chat.LoginRegistration
             * @static
             * @param {chat.LoginRegistration} message LoginRegistration
             * @param {$protobuf.IConversionOptions} [options] Conversion options
             * @returns {Object.<string,*>} Plain object
             */
            LoginRegistration.toObject = function toObject(message, options) {
                if (!options)
                    options = {};
                var object = {};
                if (options.defaults) {
                    object.name = "";
                    object.channel = "";
                }
                if (message.name != null && message.hasOwnProperty("name"))
                    object.name = message.name;
                if (message.channel != null && message.hasOwnProperty("channel"))
                    object.channel = message.channel;
                return object;
            };
    
            /**
             * Converts this LoginRegistration to JSON.
             * @function toJSON
             * @memberof chat.LoginRegistration
             * @instance
             * @returns {Object.<string,*>} JSON object
             */
            LoginRegistration.prototype.toJSON = function toJSON() {
                return this.constructor.toObject(this, $protobuf.util.toJSONOptions);
            };
    
            return LoginRegistration;
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
    
        chat.ChatContent = (function() {
    
            /**
             * Properties of a ChatContent.
             * @memberof chat
             * @interface IChatContent
             * @property {chat.Scope|null} [scope] ChatContent scope
             * @property {string|null} [message] ChatContent message
             * @property {string|null} [target] ChatContent target
             * @property {string|null} [from] ChatContent from
             */
    
            /**
             * Constructs a new ChatContent.
             * @memberof chat
             * @classdesc Represents a ChatContent.
             * @implements IChatContent
             * @constructor
             * @param {chat.IChatContent=} [properties] Properties to set
             */
            function ChatContent(properties) {
                if (properties)
                    for (var keys = Object.keys(properties), i = 0; i < keys.length; ++i)
                        if (properties[keys[i]] != null)
                            this[keys[i]] = properties[keys[i]];
            }
    
            /**
             * ChatContent scope.
             * @member {chat.Scope} scope
             * @memberof chat.ChatContent
             * @instance
             */
            ChatContent.prototype.scope = 0;
    
            /**
             * ChatContent message.
             * @member {string} message
             * @memberof chat.ChatContent
             * @instance
             */
            ChatContent.prototype.message = "";
    
            /**
             * ChatContent target.
             * @member {string} target
             * @memberof chat.ChatContent
             * @instance
             */
            ChatContent.prototype.target = "";
    
            /**
             * ChatContent from.
             * @member {string} from
             * @memberof chat.ChatContent
             * @instance
             */
            ChatContent.prototype.from = "";
    
            /**
             * Creates a new ChatContent instance using the specified properties.
             * @function create
             * @memberof chat.ChatContent
             * @static
             * @param {chat.IChatContent=} [properties] Properties to set
             * @returns {chat.ChatContent} ChatContent instance
             */
            ChatContent.create = function create(properties) {
                return new ChatContent(properties);
            };
    
            /**
             * Encodes the specified ChatContent message. Does not implicitly {@link chat.ChatContent.verify|verify} messages.
             * @function encode
             * @memberof chat.ChatContent
             * @static
             * @param {chat.IChatContent} message ChatContent message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            ChatContent.encode = function encode(message, writer) {
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
             * Encodes the specified ChatContent message, length delimited. Does not implicitly {@link chat.ChatContent.verify|verify} messages.
             * @function encodeDelimited
             * @memberof chat.ChatContent
             * @static
             * @param {chat.IChatContent} message ChatContent message or plain object to encode
             * @param {$protobuf.Writer} [writer] Writer to encode to
             * @returns {$protobuf.Writer} Writer
             */
            ChatContent.encodeDelimited = function encodeDelimited(message, writer) {
                return this.encode(message, writer).ldelim();
            };
    
            /**
             * Decodes a ChatContent message from the specified reader or buffer.
             * @function decode
             * @memberof chat.ChatContent
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @param {number} [length] Message length if known beforehand
             * @returns {chat.ChatContent} ChatContent
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            ChatContent.decode = function decode(reader, length) {
                if (!(reader instanceof $Reader))
                    reader = $Reader.create(reader);
                var end = length === undefined ? reader.len : reader.pos + length, message = new $root.chat.ChatContent();
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
             * Decodes a ChatContent message from the specified reader or buffer, length delimited.
             * @function decodeDelimited
             * @memberof chat.ChatContent
             * @static
             * @param {$protobuf.Reader|Uint8Array} reader Reader or buffer to decode from
             * @returns {chat.ChatContent} ChatContent
             * @throws {Error} If the payload is not a reader or valid buffer
             * @throws {$protobuf.util.ProtocolError} If required fields are missing
             */
            ChatContent.decodeDelimited = function decodeDelimited(reader) {
                if (!(reader instanceof $Reader))
                    reader = new $Reader(reader);
                return this.decode(reader, reader.uint32());
            };
    
            /**
             * Verifies a ChatContent message.
             * @function verify
             * @memberof chat.ChatContent
             * @static
             * @param {Object.<string,*>} message Plain object to verify
             * @returns {string|null} `null` if valid, otherwise the reason why it is not
             */
            ChatContent.verify = function verify(message) {
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
             * Creates a ChatContent message from a plain object. Also converts values to their respective internal types.
             * @function fromObject
             * @memberof chat.ChatContent
             * @static
             * @param {Object.<string,*>} object Plain object
             * @returns {chat.ChatContent} ChatContent
             */
            ChatContent.fromObject = function fromObject(object) {
                if (object instanceof $root.chat.ChatContent)
                    return object;
                var message = new $root.chat.ChatContent();
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
             * Creates a plain object from a ChatContent message. Also converts values to other types if specified.
             * @function toObject
             * @memberof chat.ChatContent
             * @static
             * @param {chat.ChatContent} message ChatContent
             * @param {$protobuf.IConversionOptions} [options] Conversion options
             * @returns {Object.<string,*>} Plain object
             */
            ChatContent.toObject = function toObject(message, options) {
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
             * Converts this ChatContent to JSON.
             * @function toJSON
             * @memberof chat.ChatContent
             * @instance
             * @returns {Object.<string,*>} JSON object
             */
            ChatContent.prototype.toJSON = function toJSON() {
                return this.constructor.toObject(this, $protobuf.util.toJSONOptions);
            };
    
            return ChatContent;
        })();
    
        return chat;
    })();

    return $root;
});
