// Copyright (c) Microsoft. All rights reserved.

import { useMsal } from '@azure/msal-react';
import { Constants } from '../Constants';
import { useAppDispatch, useAppSelector } from '../redux/app/hooks';
import { RootState } from '../redux/app/store';
import { addAlert } from '../redux/features/app/appSlice';
import { ChatState } from '../redux/features/conversations/ChatState';
import { Conversations } from '../redux/features/conversations/ConversationsState';
import {
    addConversation,
    setConversations,
    setSelectedConversation,
    updateConversation,
} from '../redux/features/conversations/conversationsSlice';
import { AuthHeaderTags } from '../redux/features/plugins/PluginsState';
import { AuthHelper } from './auth/AuthHelper';
import { AlertType } from './models/AlertType';
import { Bot } from './models/Bot';
import { AuthorRoles, ChatMessageType, IChatMessage } from './models/ChatMessage';
import { IChatSession } from './models/ChatSession';
import { IChatUser } from './models/ChatUser';
import { PlanState } from './models/Plan';
import { IAskVariables } from './semantic-kernel/model/Ask';
import { BotService } from './services/BotService';
import { ChatService } from './services/ChatService';
import { DocumentImportService } from './services/DocumentImportService';
import * as PlanUtils from './utils/PlanUtils';

import botIcon1 from '../assets/bot-icons/bot-icon-1.png';
import botIcon2 from '../assets/bot-icons/bot-icon-2.png';
import botIcon3 from '../assets/bot-icons/bot-icon-3.png';
import botIcon4 from '../assets/bot-icons/bot-icon-4.png';
import botIcon5 from '../assets/bot-icons/bot-icon-5.png';

export interface GetResponseOptions {
    messageType: ChatMessageType;
    value: string;
    chatId: string;
    contextVariables?: IAskVariables[];
}

export const useChat = () => {
    const dispatch = useAppDispatch();
    const { instance, inProgress } = useMsal();
    const account = instance.getActiveAccount();
    const { conversations } = useAppSelector((state: RootState) => state.conversations);

    const botService = new BotService(process.env.REACT_APP_BACKEND_URI as string);
    const chatService = new ChatService(process.env.REACT_APP_BACKEND_URI as string);
    const documentImportService = new DocumentImportService(process.env.REACT_APP_BACKEND_URI as string);

    const botProfilePictures: string[] = [botIcon1, botIcon2, botIcon3, botIcon4, botIcon5];

    const loggedInUser: IChatUser = {
        id: account?.homeAccountId || '',
        fullName: (account?.name ?? account?.username) || '',
        emailAddress: account?.username || '',
        photo: undefined, // TODO: Make call to Graph /me endpoint to load photo
        online: true,
        lastTypingTimestamp: 0,
    };

    const plugins = useAppSelector((state: RootState) => state.plugins);

    const getChatUserById = (id: string, chatId: string, users: IChatUser[]) => {
        if (id === `${chatId}-bot` || id.toLocaleLowerCase() === 'bot') return Constants.bot.profile;
        return users.find((user) => user.id === id);
    };

    const createChat = async () => {
        const chatTitle = `Copilot @ ${new Date().toLocaleString()}`;
        try {
            await chatService
                .createChatAsync(
                    account?.homeAccountId!,
                    account?.name ?? account?.username!,
                    chatTitle,
                    await AuthHelper.getSKaaSAccessToken(instance, inProgress),
                )
                .then(async (result: IChatSession) => {
                    const chatMessages = await chatService.getChatMessagesAsync(
                        result.id,
                        0,
                        1,
                        await AuthHelper.getSKaaSAccessToken(instance, inProgress),
                    );

                    const newChat: ChatState = {
                        id: result.id,
                        title: result.title,
                        messages: chatMessages,
                        users: [loggedInUser],
                        botProfilePicture: getBotProfilePicture(Object.keys(conversations).length),
                        input: '',
                    };

                    dispatch(addConversation(newChat));
                    return newChat.id;
                });
        } catch (e: any) {
            const errorMessage = `Unable to create new chat. Details: ${e.message ?? e}`;
            dispatch(addAlert({ message: errorMessage, type: AlertType.Error }));
        }
    };

    const getResponse = async ({ messageType, value, chatId, contextVariables }: GetResponseOptions) => {
        const ask = {
            input: value,
            variables: [
                {
                    key: 'userId',
                    value: account?.homeAccountId!,
                },
                {
                    key: 'userName',
                    value: account?.name ?? account?.username!,
                },
                {
                    key: 'chatId',
                    value: chatId,
                },
                {
                    key: 'messageType',
                    value: messageType.toString(),
                },
            ],
        };

        if (contextVariables) {
            ask.variables.push(...contextVariables);
        }

        try {
            var result = await chatService.getBotResponseAsync(
                ask,
                await AuthHelper.getSKaaSAccessToken(instance, inProgress),
                getEnabledPlugins(),
            );

            const messageResult: IChatMessage = {
                type: (result.variables.find((v) => v.key === 'messageType')?.value ??
                    ChatMessageType.Message) as ChatMessageType,
                timestamp: new Date().getTime(),
                userName: 'bot',
                userId: 'bot',
                content: result.value,
                prompt: result.variables.find((v) => v.key === 'prompt')?.value,
                authorRole: AuthorRoles.Bot,
                id: result.variables.find((v) => v.key === 'messageId')?.value,
                state: PlanUtils.isPlan(result.value) ? PlanState.PlanApprovalRequired : PlanState.NoOp,
            };

            dispatch(updateConversation({ message: messageResult, chatId: chatId }));
        } catch (e: any) {
            const errorMessage = `Unable to generate bot response. Details: ${e.message ?? e}`;
            dispatch(addAlert({ message: errorMessage, type: AlertType.Error }));
        }
    };

    const loadChats = async () => {
        try {
            const chatSessions = await chatService.getAllChatsAsync(
                account?.homeAccountId!,
                await AuthHelper.getSKaaSAccessToken(instance, inProgress),
            );

            if (chatSessions.length > 0) {
                const loadedConversations: Conversations = {};
                for (const index in chatSessions) {
                    const chatSession = chatSessions[index];
                    const chatMessages = await chatService.getChatMessagesAsync(
                        chatSession.id,
                        0,
                        100,
                        await AuthHelper.getSKaaSAccessToken(instance, inProgress),
                    );

                    loadedConversations[chatSession.id] = {
                        id: chatSession.id,
                        title: chatSession.title,
                        users: [loggedInUser],
                        messages: chatMessages,
                        botProfilePicture: getBotProfilePicture(Object.keys(loadedConversations).length),
                        input: '',
                    };
                }

                dispatch(setConversations(loadedConversations));
                dispatch(setSelectedConversation(chatSessions[0].id));
            } else {
                // No chats exist, create first chat window
                await createChat();
            }

            return true;
        } catch (e: any) {
            const errorMessage = `Unable to load chats. Details: ${e.message ?? e}`;
            dispatch(addAlert({ message: errorMessage, type: AlertType.Error }));

            return false;
        }
    };

    const downloadBot = async (chatId: string) => {
        try {
            return botService.downloadAsync(chatId, await AuthHelper.getSKaaSAccessToken(instance, inProgress));
        } catch (e: any) {
            const errorMessage = `Unable to download the bot. Details: ${e.message ?? e}`;
            dispatch(addAlert({ message: errorMessage, type: AlertType.Error }));
        }
    };

    const uploadBot = async (bot: Bot) => {
        botService
            .uploadAsync(bot, account?.homeAccountId || '', await AuthHelper.getSKaaSAccessToken(instance, inProgress))
            .then(async (chatSession: IChatSession) => {
                const chatMessages = await chatService.getChatMessagesAsync(
                    chatSession.id,
                    0,
                    100,
                    await AuthHelper.getSKaaSAccessToken(instance, inProgress),
                );

                const newChat = {
                    id: chatSession.id,
                    title: chatSession.title,
                    users: [loggedInUser],
                    messages: chatMessages,
                    botProfilePicture: getBotProfilePicture(Object.keys(conversations).length),
                    lastUpdatedTimestamp: new Date().getTime(),
                };

                dispatch(addConversation(newChat));
            })
            .catch((e: any) => {
                const errorMessage = `Unable to upload the bot. Details: ${e.message ?? e}`;
                dispatch(addAlert({ message: errorMessage, type: AlertType.Error }));
            });
    };

    const getBotProfilePicture = (index: number) => {
        return botProfilePictures[index % botProfilePictures.length];
    };

    const getChatMemorySources = async (chatId: string) => {
        try {
            return await chatService.getChatMemorySourcesAsync(
                chatId,
                await AuthHelper.getSKaaSAccessToken(instance, inProgress),
            );
        } catch (e: any) {
            const errorMessage = `Unable to get chat files. Details: ${e.message ?? e}`;
            dispatch(addAlert({ message: errorMessage, type: AlertType.Error }));
        }

        return [];
    };

    const importDocument = async (chatId: string, file: File) => {
        try {
            const message = await documentImportService.importDocumentAsync(
                account!.homeAccountId!,
                (account!.name ?? account!.username) as string,
                chatId,
                file,
                await AuthHelper.getSKaaSAccessToken(instance, inProgress),
            );

            dispatch(updateConversation({ message, chatId }));
        } catch (e: any) {
            const errorMessage = `Failed to upload document. Details: ${e.message ?? e}`;
            dispatch(addAlert({ message: errorMessage, type: AlertType.Error }));
        }
    };

    /*
     * Once enabled, each plugin will have a custom dedicated header in every Semantic Kernel request
     * containing respective auth information (i.e., token, encoded client info, etc.)
     * that the server can use to authenticate to the downstream APIs
     */
    const getEnabledPlugins = () => {
        const enabledPlugins: { headerTag: AuthHeaderTags; authData: string; apiProperties?: any }[] = [];

        Object.entries(plugins).map((entry) => {
            const plugin = entry[1];

            if (plugin.enabled) {
                enabledPlugins.push({
                    headerTag: plugin.headerTag,
                    authData: plugin.authData!,
                    apiProperties: plugin.apiProperties,
                });
            }

            return entry;
        });

        return enabledPlugins;
    };

    return {
        getChatUserById,
        createChat,
        loadChats,
        getResponse,
        downloadBot,
        uploadBot,
        getChatMemorySources,
        importDocument,
    };
};
