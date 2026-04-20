import * as React from "react";
import { Popover, ConfigProvider } from "antd";
import "./index.css";

export default function ConfirmAction({
    children,
    confirmationMessage,
    action,
    containerStyles,
    disabled,
    hex = "var(--highlight-primary-1)",
}: {
    children: React.ReactNode;
    confirmationMessage: string;
    action: () => any;
    containerStyles?: React.CSSProperties;
    disabled?: boolean;
    hex?: string;
}) {
    if (disabled) return <>{children}</>;
    return (
        <span style={{ ...containerStyles, "--hex": hex } as React.CSSProperties}>
            <Message message={confirmationMessage} action={action} hex={hex}>
                {children}
            </Message>
        </span>
    );
}

export function Message({
    message,
    children,
    action,
    hex,
}: {
    message: string;
    children: React.ReactNode;
    action: () => any;
    hex: string;
}) {
    const [open, setOpen] = React.useState(false);
    if (!message) return null;

    const Content = (
        <div className="confirm-action" style={{ maxWidth: 200, "--hex": hex } as React.CSSProperties}>
            <p style={{ fontSize: 12 }}>{message}</p>
            <div className="dueling-buttons">
                <button onClick={() => setOpen(false)}>No</button>
                <button onClick={() => {
                    if (action && typeof action === "function") action();
                    setOpen(false);
                }}>Yes</button>
            </div>
        </div>
    );

    return (
        // <ConfigProvider
        //     theme={{
        //         components: {
        //             Popover: {
        //                 zIndexPopup: 6000,
        //             },
        //         },
        //     }}
        // >
        <Popover
            content={Content}
            trigger="click"
            open={open}
            onOpenChange={setOpen}
        >
            <span>{children}</span>
        </Popover>
        // </ConfigProvider>
    );
}
