import * as React from "react";
import "./index.css";
import { Popover } from "antd";

interface ClickableElement {
    element?: React.ReactNode | string;
    action?: (parameters?: any) => any;
    helpText?: string;
    position?: "left" | "right";
}

export interface FieldProperties {
    label?: string;
    labelIcon?: ClickableElement;
    labelAction?: (parameters?: any) => any;
    value: string | number;
    valueIcon?: ClickableElement;
    valueAction?: (parameters?: any) => any;
    orientation?: "vertical" | "horizontal";
    size?: "default" | "small";
    customValueStyle?: React.CSSProperties;
    customLabelStyle?: React.CSSProperties;
    valueTextAlign?: "left" | "right" | "center";
    labelTextAlign?: "left" | "right" | "center";
}

export default function Field({
    label,
    labelIcon,
    labelAction,
    value,
    valueIcon,
    valueAction,
    orientation = "vertical",
    size = "default",
    customValueStyle,
    customLabelStyle,
    valueTextAlign = "left",
    labelTextAlign = "left",
}: FieldProperties) {
    return (
        <div className={`custom-field ${orientation} ${size}`}>
            {(label || labelIcon) && (
                <span
                    className={`custom-field-label ${labelTextAlign}`}
                    style={{ ...customLabelStyle }}
                >
                    {labelIcon && labelIcon.position === "left" && (
                        <Popover content={labelIcon.helpText}>
                            <button
                                disabled={!labelIcon.action}
                                className={`default-btn field-icon-wrapper${labelIcon.action ? " clickable" : ""
                                    }`}
                                onClick={labelIcon && typeof labelIcon.action === "function" ? labelIcon.action : () => null}
                            >
                                {labelIcon.element}
                            </button>
                        </Popover>
                    )}
                    {label && (
                        <button
                            className={`default-btn${labelAction ? " clickable" : ""}`}
                            onClick={labelAction ? () => labelAction() : () => null}
                            disabled={!labelAction}
                        >
                            {label}
                        </button>
                    )}
                    {labelIcon &&
                        (labelIcon.position === "right" || !labelIcon.position) && (
                            <Popover content={labelIcon.helpText}>
                                <button
                                    disabled={!labelIcon.action}
                                    className={`default-btn field-icon-wrapper${labelIcon.action ? " clickable" : ""
                                        }`}
                                    onClick={labelIcon && typeof labelIcon.action === "function" ? labelIcon.action : () => null}
                                >
                                    {labelIcon.element}
                                </button>
                            </Popover>
                        )}
                </span>
            )}

            {(value || valueIcon) && (
                <span
                    className={`custom-field-value ${valueTextAlign}`}
                    style={{ ...customValueStyle }}
                >
                    {valueIcon && valueIcon.position === "left" && (
                        <Popover content={valueIcon.helpText}>
                            <button
                                disabled={!valueIcon.action}
                                className={`default-btn icon-wrapper${valueIcon.action ? " clickable" : ""
                                    }`}
                            >
                                {valueIcon.element}
                            </button>
                        </Popover>
                    )}
                    {value && (
                        <button
                            className={`default-btn${valueAction ? " clickable" : ""}`}
                            onClick={valueAction ? () => valueAction() : () => null}
                            disabled={!valueAction}
                        >
                            {value}
                        </button>
                    )}
                    {valueIcon &&
                        (valueIcon.position === "right" || !valueIcon.position) && (
                            <Popover content={valueIcon.helpText}>
                                <button
                                    disabled={!valueIcon.action}
                                    className={`default-btn icon-wrapper${valueIcon.action ? " clickable" : ""
                                        }`}
                                >
                                    {valueIcon.element}
                                </button>
                            </Popover>
                        )}
                </span>
            )}
        </div>
    );
}
