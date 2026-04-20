import * as React from "react";
import { message } from "antd";

interface INotificationContext {
    setNotification: (
        message: string,
        type?: "error" | "warning" | "success" | "info"
    ) => void;
}

const NotificationContext = React.createContext({} as INotificationContext);

export function useNotification() {
    return React.useContext(NotificationContext);
}

export function NotificationProvider({
    children,
}: {
    children: React.ReactNode;
}) {
    const [messageApi, contextHolder] = message.useMessage();

    function setNotification(
        message: string,
        type?: "error" | "warning" | "success" | "info"
    ) {
        messageApi.open({
            type: type || "info",
            content: message,
        });
    }

    const value = {
        setNotification,
    };

    return (
        <>
            {contextHolder}
            <NotificationContext.Provider value={value}>
                {children}
            </NotificationContext.Provider>
        </>
    );
}
