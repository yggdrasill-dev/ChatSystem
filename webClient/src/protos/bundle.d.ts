import * as $protobuf from "protobufjs";
/** Namespace chat. */
export namespace chat {

    /** Properties of a Packet. */
    interface IPacket {

        /** Packet subject */
        subject?: (string|null);

        /** Packet payload */
        payload?: (Uint8Array|null);
    }

    /** Represents a Packet. */
    class Packet implements IPacket {

        /**
         * Constructs a new Packet.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.IPacket);

        /** Packet subject. */
        public subject: string;

        /** Packet payload. */
        public payload: Uint8Array;

        /**
         * Creates a new Packet instance using the specified properties.
         * @param [properties] Properties to set
         * @returns Packet instance
         */
        public static create(properties?: chat.IPacket): chat.Packet;

        /**
         * Encodes the specified Packet message. Does not implicitly {@link chat.Packet.verify|verify} messages.
         * @param message Packet message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encode(message: chat.IPacket, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Encodes the specified Packet message, length delimited. Does not implicitly {@link chat.Packet.verify|verify} messages.
         * @param message Packet message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encodeDelimited(message: chat.IPacket, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Decodes a Packet message from the specified reader or buffer.
         * @param reader Reader or buffer to decode from
         * @param [length] Message length if known beforehand
         * @returns Packet
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decode(reader: ($protobuf.Reader|Uint8Array), length?: number): chat.Packet;

        /**
         * Decodes a Packet message from the specified reader or buffer, length delimited.
         * @param reader Reader or buffer to decode from
         * @returns Packet
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decodeDelimited(reader: ($protobuf.Reader|Uint8Array)): chat.Packet;

        /**
         * Verifies a Packet message.
         * @param message Plain object to verify
         * @returns `null` if valid, otherwise the reason why it is not
         */
        public static verify(message: { [k: string]: any }): (string|null);

        /**
         * Creates a Packet message from a plain object. Also converts values to their respective internal types.
         * @param object Plain object
         * @returns Packet
         */
        public static fromObject(object: { [k: string]: any }): chat.Packet;

        /**
         * Creates a plain object from a Packet message. Also converts values to other types if specified.
         * @param message Packet
         * @param [options] Conversion options
         * @returns Plain object
         */
        public static toObject(message: chat.Packet, options?: $protobuf.IConversionOptions): { [k: string]: any };

        /**
         * Converts this Packet to JSON.
         * @returns JSON object
         */
        public toJSON(): { [k: string]: any };
    }

    /** LoginStatus enum. */
    enum LoginStatus {
        LOGINSTATUS_REJECT = 0,
        LOGINSTATUS_ACCPET = 1
    }

    /** Properties of a LoginReply. */
    interface ILoginReply {

        /** LoginReply status */
        status?: (chat.LoginStatus|null);

        /** LoginReply name */
        name?: (string|null);
    }

    /** Represents a LoginReply. */
    class LoginReply implements ILoginReply {

        /**
         * Constructs a new LoginReply.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.ILoginReply);

        /** LoginReply status. */
        public status: chat.LoginStatus;

        /** LoginReply name. */
        public name: string;

        /**
         * Creates a new LoginReply instance using the specified properties.
         * @param [properties] Properties to set
         * @returns LoginReply instance
         */
        public static create(properties?: chat.ILoginReply): chat.LoginReply;

        /**
         * Encodes the specified LoginReply message. Does not implicitly {@link chat.LoginReply.verify|verify} messages.
         * @param message LoginReply message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encode(message: chat.ILoginReply, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Encodes the specified LoginReply message, length delimited. Does not implicitly {@link chat.LoginReply.verify|verify} messages.
         * @param message LoginReply message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encodeDelimited(message: chat.ILoginReply, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Decodes a LoginReply message from the specified reader or buffer.
         * @param reader Reader or buffer to decode from
         * @param [length] Message length if known beforehand
         * @returns LoginReply
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decode(reader: ($protobuf.Reader|Uint8Array), length?: number): chat.LoginReply;

        /**
         * Decodes a LoginReply message from the specified reader or buffer, length delimited.
         * @param reader Reader or buffer to decode from
         * @returns LoginReply
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decodeDelimited(reader: ($protobuf.Reader|Uint8Array)): chat.LoginReply;

        /**
         * Verifies a LoginReply message.
         * @param message Plain object to verify
         * @returns `null` if valid, otherwise the reason why it is not
         */
        public static verify(message: { [k: string]: any }): (string|null);

        /**
         * Creates a LoginReply message from a plain object. Also converts values to their respective internal types.
         * @param object Plain object
         * @returns LoginReply
         */
        public static fromObject(object: { [k: string]: any }): chat.LoginReply;

        /**
         * Creates a plain object from a LoginReply message. Also converts values to other types if specified.
         * @param message LoginReply
         * @param [options] Conversion options
         * @returns Plain object
         */
        public static toObject(message: chat.LoginReply, options?: $protobuf.IConversionOptions): { [k: string]: any };

        /**
         * Converts this LoginReply to JSON.
         * @returns JSON object
         */
        public toJSON(): { [k: string]: any };
    }

    /** Scope enum. */
    enum Scope {
        SCOPE_NONE = 0,
        SCOPE_PERSON = 1,
        SCOPE_ROOM = 2,
        SCOPE_SYSTEM = 3
    }

    /** Properties of a ChatMessage. */
    interface IChatMessage {

        /** ChatMessage scope */
        scope?: (chat.Scope|null);

        /** ChatMessage message */
        message?: (string|null);

        /** ChatMessage target */
        target?: (string|null);

        /** ChatMessage from */
        from?: (string|null);
    }

    /** Represents a ChatMessage. */
    class ChatMessage implements IChatMessage {

        /**
         * Constructs a new ChatMessage.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.IChatMessage);

        /** ChatMessage scope. */
        public scope: chat.Scope;

        /** ChatMessage message. */
        public message: string;

        /** ChatMessage target. */
        public target: string;

        /** ChatMessage from. */
        public from: string;

        /**
         * Creates a new ChatMessage instance using the specified properties.
         * @param [properties] Properties to set
         * @returns ChatMessage instance
         */
        public static create(properties?: chat.IChatMessage): chat.ChatMessage;

        /**
         * Encodes the specified ChatMessage message. Does not implicitly {@link chat.ChatMessage.verify|verify} messages.
         * @param message ChatMessage message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encode(message: chat.IChatMessage, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Encodes the specified ChatMessage message, length delimited. Does not implicitly {@link chat.ChatMessage.verify|verify} messages.
         * @param message ChatMessage message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encodeDelimited(message: chat.IChatMessage, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Decodes a ChatMessage message from the specified reader or buffer.
         * @param reader Reader or buffer to decode from
         * @param [length] Message length if known beforehand
         * @returns ChatMessage
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decode(reader: ($protobuf.Reader|Uint8Array), length?: number): chat.ChatMessage;

        /**
         * Decodes a ChatMessage message from the specified reader or buffer, length delimited.
         * @param reader Reader or buffer to decode from
         * @returns ChatMessage
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decodeDelimited(reader: ($protobuf.Reader|Uint8Array)): chat.ChatMessage;

        /**
         * Verifies a ChatMessage message.
         * @param message Plain object to verify
         * @returns `null` if valid, otherwise the reason why it is not
         */
        public static verify(message: { [k: string]: any }): (string|null);

        /**
         * Creates a ChatMessage message from a plain object. Also converts values to their respective internal types.
         * @param object Plain object
         * @returns ChatMessage
         */
        public static fromObject(object: { [k: string]: any }): chat.ChatMessage;

        /**
         * Creates a plain object from a ChatMessage message. Also converts values to other types if specified.
         * @param message ChatMessage
         * @param [options] Conversion options
         * @returns Plain object
         */
        public static toObject(message: chat.ChatMessage, options?: $protobuf.IConversionOptions): { [k: string]: any };

        /**
         * Converts this ChatMessage to JSON.
         * @returns JSON object
         */
        public toJSON(): { [k: string]: any };
    }

    /** Properties of a PlayerList. */
    interface IPlayerList {

        /** PlayerList room */
        room?: (string|null);

        /** PlayerList players */
        players?: (string[]|null);
    }

    /** Represents a PlayerList. */
    class PlayerList implements IPlayerList {

        /**
         * Constructs a new PlayerList.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.IPlayerList);

        /** PlayerList room. */
        public room: string;

        /** PlayerList players. */
        public players: string[];

        /**
         * Creates a new PlayerList instance using the specified properties.
         * @param [properties] Properties to set
         * @returns PlayerList instance
         */
        public static create(properties?: chat.IPlayerList): chat.PlayerList;

        /**
         * Encodes the specified PlayerList message. Does not implicitly {@link chat.PlayerList.verify|verify} messages.
         * @param message PlayerList message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encode(message: chat.IPlayerList, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Encodes the specified PlayerList message, length delimited. Does not implicitly {@link chat.PlayerList.verify|verify} messages.
         * @param message PlayerList message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encodeDelimited(message: chat.IPlayerList, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Decodes a PlayerList message from the specified reader or buffer.
         * @param reader Reader or buffer to decode from
         * @param [length] Message length if known beforehand
         * @returns PlayerList
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decode(reader: ($protobuf.Reader|Uint8Array), length?: number): chat.PlayerList;

        /**
         * Decodes a PlayerList message from the specified reader or buffer, length delimited.
         * @param reader Reader or buffer to decode from
         * @returns PlayerList
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decodeDelimited(reader: ($protobuf.Reader|Uint8Array)): chat.PlayerList;

        /**
         * Verifies a PlayerList message.
         * @param message Plain object to verify
         * @returns `null` if valid, otherwise the reason why it is not
         */
        public static verify(message: { [k: string]: any }): (string|null);

        /**
         * Creates a PlayerList message from a plain object. Also converts values to their respective internal types.
         * @param object Plain object
         * @returns PlayerList
         */
        public static fromObject(object: { [k: string]: any }): chat.PlayerList;

        /**
         * Creates a plain object from a PlayerList message. Also converts values to other types if specified.
         * @param message PlayerList
         * @param [options] Conversion options
         * @returns Plain object
         */
        public static toObject(message: chat.PlayerList, options?: $protobuf.IConversionOptions): { [k: string]: any };

        /**
         * Converts this PlayerList to JSON.
         * @returns JSON object
         */
        public toJSON(): { [k: string]: any };
    }

    /** Properties of a Room. */
    interface IRoom {

        /** Room name */
        name?: (string|null);

        /** Room hasPassword */
        hasPassword?: (boolean|null);
    }

    /** Represents a Room. */
    class Room implements IRoom {

        /**
         * Constructs a new Room.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.IRoom);

        /** Room name. */
        public name: string;

        /** Room hasPassword. */
        public hasPassword: boolean;

        /**
         * Creates a new Room instance using the specified properties.
         * @param [properties] Properties to set
         * @returns Room instance
         */
        public static create(properties?: chat.IRoom): chat.Room;

        /**
         * Encodes the specified Room message. Does not implicitly {@link chat.Room.verify|verify} messages.
         * @param message Room message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encode(message: chat.IRoom, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Encodes the specified Room message, length delimited. Does not implicitly {@link chat.Room.verify|verify} messages.
         * @param message Room message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encodeDelimited(message: chat.IRoom, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Decodes a Room message from the specified reader or buffer.
         * @param reader Reader or buffer to decode from
         * @param [length] Message length if known beforehand
         * @returns Room
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decode(reader: ($protobuf.Reader|Uint8Array), length?: number): chat.Room;

        /**
         * Decodes a Room message from the specified reader or buffer, length delimited.
         * @param reader Reader or buffer to decode from
         * @returns Room
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decodeDelimited(reader: ($protobuf.Reader|Uint8Array)): chat.Room;

        /**
         * Verifies a Room message.
         * @param message Plain object to verify
         * @returns `null` if valid, otherwise the reason why it is not
         */
        public static verify(message: { [k: string]: any }): (string|null);

        /**
         * Creates a Room message from a plain object. Also converts values to their respective internal types.
         * @param object Plain object
         * @returns Room
         */
        public static fromObject(object: { [k: string]: any }): chat.Room;

        /**
         * Creates a plain object from a Room message. Also converts values to other types if specified.
         * @param message Room
         * @param [options] Conversion options
         * @returns Plain object
         */
        public static toObject(message: chat.Room, options?: $protobuf.IConversionOptions): { [k: string]: any };

        /**
         * Converts this Room to JSON.
         * @returns JSON object
         */
        public toJSON(): { [k: string]: any };
    }

    /** Properties of a RoomList. */
    interface IRoomList {

        /** RoomList rooms */
        rooms?: (chat.IRoom[]|null);
    }

    /** Represents a RoomList. */
    class RoomList implements IRoomList {

        /**
         * Constructs a new RoomList.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.IRoomList);

        /** RoomList rooms. */
        public rooms: chat.IRoom[];

        /**
         * Creates a new RoomList instance using the specified properties.
         * @param [properties] Properties to set
         * @returns RoomList instance
         */
        public static create(properties?: chat.IRoomList): chat.RoomList;

        /**
         * Encodes the specified RoomList message. Does not implicitly {@link chat.RoomList.verify|verify} messages.
         * @param message RoomList message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encode(message: chat.IRoomList, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Encodes the specified RoomList message, length delimited. Does not implicitly {@link chat.RoomList.verify|verify} messages.
         * @param message RoomList message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encodeDelimited(message: chat.IRoomList, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Decodes a RoomList message from the specified reader or buffer.
         * @param reader Reader or buffer to decode from
         * @param [length] Message length if known beforehand
         * @returns RoomList
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decode(reader: ($protobuf.Reader|Uint8Array), length?: number): chat.RoomList;

        /**
         * Decodes a RoomList message from the specified reader or buffer, length delimited.
         * @param reader Reader or buffer to decode from
         * @returns RoomList
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decodeDelimited(reader: ($protobuf.Reader|Uint8Array)): chat.RoomList;

        /**
         * Verifies a RoomList message.
         * @param message Plain object to verify
         * @returns `null` if valid, otherwise the reason why it is not
         */
        public static verify(message: { [k: string]: any }): (string|null);

        /**
         * Creates a RoomList message from a plain object. Also converts values to their respective internal types.
         * @param object Plain object
         * @returns RoomList
         */
        public static fromObject(object: { [k: string]: any }): chat.RoomList;

        /**
         * Creates a plain object from a RoomList message. Also converts values to other types if specified.
         * @param message RoomList
         * @param [options] Conversion options
         * @returns Plain object
         */
        public static toObject(message: chat.RoomList, options?: $protobuf.IConversionOptions): { [k: string]: any };

        /**
         * Converts this RoomList to JSON.
         * @returns JSON object
         */
        public toJSON(): { [k: string]: any };
    }

    /** Properties of a JoinRoom. */
    interface IJoinRoom {

        /** JoinRoom room */
        room?: (string|null);

        /** JoinRoom password */
        password?: (string|null);
    }

    /** Represents a JoinRoom. */
    class JoinRoom implements IJoinRoom {

        /**
         * Constructs a new JoinRoom.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.IJoinRoom);

        /** JoinRoom room. */
        public room: string;

        /** JoinRoom password. */
        public password: string;

        /**
         * Creates a new JoinRoom instance using the specified properties.
         * @param [properties] Properties to set
         * @returns JoinRoom instance
         */
        public static create(properties?: chat.IJoinRoom): chat.JoinRoom;

        /**
         * Encodes the specified JoinRoom message. Does not implicitly {@link chat.JoinRoom.verify|verify} messages.
         * @param message JoinRoom message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encode(message: chat.IJoinRoom, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Encodes the specified JoinRoom message, length delimited. Does not implicitly {@link chat.JoinRoom.verify|verify} messages.
         * @param message JoinRoom message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encodeDelimited(message: chat.IJoinRoom, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Decodes a JoinRoom message from the specified reader or buffer.
         * @param reader Reader or buffer to decode from
         * @param [length] Message length if known beforehand
         * @returns JoinRoom
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decode(reader: ($protobuf.Reader|Uint8Array), length?: number): chat.JoinRoom;

        /**
         * Decodes a JoinRoom message from the specified reader or buffer, length delimited.
         * @param reader Reader or buffer to decode from
         * @returns JoinRoom
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decodeDelimited(reader: ($protobuf.Reader|Uint8Array)): chat.JoinRoom;

        /**
         * Verifies a JoinRoom message.
         * @param message Plain object to verify
         * @returns `null` if valid, otherwise the reason why it is not
         */
        public static verify(message: { [k: string]: any }): (string|null);

        /**
         * Creates a JoinRoom message from a plain object. Also converts values to their respective internal types.
         * @param object Plain object
         * @returns JoinRoom
         */
        public static fromObject(object: { [k: string]: any }): chat.JoinRoom;

        /**
         * Creates a plain object from a JoinRoom message. Also converts values to other types if specified.
         * @param message JoinRoom
         * @param [options] Conversion options
         * @returns Plain object
         */
        public static toObject(message: chat.JoinRoom, options?: $protobuf.IConversionOptions): { [k: string]: any };

        /**
         * Converts this JoinRoom to JSON.
         * @returns JSON object
         */
        public toJSON(): { [k: string]: any };
    }

    /** JoinRoomStatus enum. */
    enum JoinRoomStatus {
        JOINROOMSTATUS_REJECT = 0,
        JOINROOMSTATUS_ACCPET = 1
    }

    /** Properties of a JoinRoomReply. */
    interface IJoinRoomReply {

        /** JoinRoomReply status */
        status?: (chat.JoinRoomStatus|null);

        /** JoinRoomReply reason */
        reason?: (string|null);
    }

    /** Represents a JoinRoomReply. */
    class JoinRoomReply implements IJoinRoomReply {

        /**
         * Constructs a new JoinRoomReply.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.IJoinRoomReply);

        /** JoinRoomReply status. */
        public status: chat.JoinRoomStatus;

        /** JoinRoomReply reason. */
        public reason: string;

        /**
         * Creates a new JoinRoomReply instance using the specified properties.
         * @param [properties] Properties to set
         * @returns JoinRoomReply instance
         */
        public static create(properties?: chat.IJoinRoomReply): chat.JoinRoomReply;

        /**
         * Encodes the specified JoinRoomReply message. Does not implicitly {@link chat.JoinRoomReply.verify|verify} messages.
         * @param message JoinRoomReply message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encode(message: chat.IJoinRoomReply, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Encodes the specified JoinRoomReply message, length delimited. Does not implicitly {@link chat.JoinRoomReply.verify|verify} messages.
         * @param message JoinRoomReply message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encodeDelimited(message: chat.IJoinRoomReply, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Decodes a JoinRoomReply message from the specified reader or buffer.
         * @param reader Reader or buffer to decode from
         * @param [length] Message length if known beforehand
         * @returns JoinRoomReply
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decode(reader: ($protobuf.Reader|Uint8Array), length?: number): chat.JoinRoomReply;

        /**
         * Decodes a JoinRoomReply message from the specified reader or buffer, length delimited.
         * @param reader Reader or buffer to decode from
         * @returns JoinRoomReply
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decodeDelimited(reader: ($protobuf.Reader|Uint8Array)): chat.JoinRoomReply;

        /**
         * Verifies a JoinRoomReply message.
         * @param message Plain object to verify
         * @returns `null` if valid, otherwise the reason why it is not
         */
        public static verify(message: { [k: string]: any }): (string|null);

        /**
         * Creates a JoinRoomReply message from a plain object. Also converts values to their respective internal types.
         * @param object Plain object
         * @returns JoinRoomReply
         */
        public static fromObject(object: { [k: string]: any }): chat.JoinRoomReply;

        /**
         * Creates a plain object from a JoinRoomReply message. Also converts values to other types if specified.
         * @param message JoinRoomReply
         * @param [options] Conversion options
         * @returns Plain object
         */
        public static toObject(message: chat.JoinRoomReply, options?: $protobuf.IConversionOptions): { [k: string]: any };

        /**
         * Converts this JoinRoomReply to JSON.
         * @returns JSON object
         */
        public toJSON(): { [k: string]: any };
    }
}
