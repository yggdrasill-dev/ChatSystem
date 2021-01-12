import * as $protobuf from "protobufjs";
/** Namespace chat. */
export namespace chat {

    /** Properties of a ChatMessage. */
    interface IChatMessage {

        /** ChatMessage subject */
        subject?: (string|null);

        /** ChatMessage payload */
        payload?: (Uint8Array|null);
    }

    /** Represents a ChatMessage. */
    class ChatMessage implements IChatMessage {

        /**
         * Constructs a new ChatMessage.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.IChatMessage);

        /** ChatMessage subject. */
        public subject: string;

        /** ChatMessage payload. */
        public payload: Uint8Array;

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

    /** Properties of a LoginRegistration. */
    interface ILoginRegistration {

        /** LoginRegistration name */
        name?: (string|null);

        /** LoginRegistration channel */
        channel?: (string|null);
    }

    /** Represents a LoginRegistration. */
    class LoginRegistration implements ILoginRegistration {

        /**
         * Constructs a new LoginRegistration.
         * @param [properties] Properties to set
         */
        constructor(properties?: chat.ILoginRegistration);

        /** LoginRegistration name. */
        public name: string;

        /** LoginRegistration channel. */
        public channel: string;

        /**
         * Creates a new LoginRegistration instance using the specified properties.
         * @param [properties] Properties to set
         * @returns LoginRegistration instance
         */
        public static create(properties?: chat.ILoginRegistration): chat.LoginRegistration;

        /**
         * Encodes the specified LoginRegistration message. Does not implicitly {@link chat.LoginRegistration.verify|verify} messages.
         * @param message LoginRegistration message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encode(message: chat.ILoginRegistration, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Encodes the specified LoginRegistration message, length delimited. Does not implicitly {@link chat.LoginRegistration.verify|verify} messages.
         * @param message LoginRegistration message or plain object to encode
         * @param [writer] Writer to encode to
         * @returns Writer
         */
        public static encodeDelimited(message: chat.ILoginRegistration, writer?: $protobuf.Writer): $protobuf.Writer;

        /**
         * Decodes a LoginRegistration message from the specified reader or buffer.
         * @param reader Reader or buffer to decode from
         * @param [length] Message length if known beforehand
         * @returns LoginRegistration
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decode(reader: ($protobuf.Reader|Uint8Array), length?: number): chat.LoginRegistration;

        /**
         * Decodes a LoginRegistration message from the specified reader or buffer, length delimited.
         * @param reader Reader or buffer to decode from
         * @returns LoginRegistration
         * @throws {Error} If the payload is not a reader or valid buffer
         * @throws {$protobuf.util.ProtocolError} If required fields are missing
         */
        public static decodeDelimited(reader: ($protobuf.Reader|Uint8Array)): chat.LoginRegistration;

        /**
         * Verifies a LoginRegistration message.
         * @param message Plain object to verify
         * @returns `null` if valid, otherwise the reason why it is not
         */
        public static verify(message: { [k: string]: any }): (string|null);

        /**
         * Creates a LoginRegistration message from a plain object. Also converts values to their respective internal types.
         * @param object Plain object
         * @returns LoginRegistration
         */
        public static fromObject(object: { [k: string]: any }): chat.LoginRegistration;

        /**
         * Creates a plain object from a LoginRegistration message. Also converts values to other types if specified.
         * @param message LoginRegistration
         * @param [options] Conversion options
         * @returns Plain object
         */
        public static toObject(message: chat.LoginRegistration, options?: $protobuf.IConversionOptions): { [k: string]: any };

        /**
         * Converts this LoginRegistration to JSON.
         * @returns JSON object
         */
        public toJSON(): { [k: string]: any };
    }
}
