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
    
        return chat;
    })();

    return $root;
});
