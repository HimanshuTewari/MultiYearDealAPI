import * as React from 'react';
import { InputNumber, InputNumberProps } from 'antd';
import { formatDollarValue, formatNumberValue } from '../../../utilities';

type FormatType = 'dollar' | 'number';

interface FormattedInputNumberProps extends InputNumberProps {
    formatType: FormatType;
    currencyIsoCode?: string;
}

export default function FormattedInputNumber({
    formatType,
    currencyIsoCode = 'USD',
    ...props
}: FormattedInputNumberProps) {
    const [focused, setFocused] = React.useState(false);

    function format(n: number): string {
        return formatType === 'dollar'
            ? formatDollarValue(n, currencyIsoCode)
            : formatNumberValue(n);
    }

    function parse(input?: string | number): number {
        const str = String(input ?? '').replace(/[^0-9.-]/g, '');
        const num = Number(str);
        return isNaN(num) ? 0 : num;
    }


    return (
        <InputNumber
            {...props}
            formatter={(val) => {
                if (focused) return String(val ?? '');
                const num = Number(val);
                return !isNaN(num) ? format(num) : '';
            }}
            parser={parse}
            onFocus={(e) => {
                setFocused(true);
                props.onFocus?.(e);
            }}
            onBlur={(e) => {
                setFocused(false);
                props.onBlur?.(e);
            }}
        />
    );
}
