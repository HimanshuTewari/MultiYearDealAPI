import * as React from "react";
import { Cascader, message } from "antd";
import type { DefaultOptionType } from "antd/es/cascader";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import "./index.css";

export default function CascadingSearch({
    items,
    hierarchy,
    value,
    onChange
}: {
    items: Array<Record<string, any>>;
    hierarchy: Array<string>;
    value: Array<string>;
    onChange: (value: Array<string | number | null>) => void;
}) {

    const options = React.useMemo(() => {
        try {
            const buildTree = (records: Array<Record<string, any>>, levels: Array<string>): Array<{ value: string; label: string; children?: Array<any> }> => {
                if (levels.length === 0) return [];

                const [currentLevel, ...remainingLevels] = levels;
                const grouped = records.reduce((acc: Record<string, Array<Record<string, any>>>, record) => {
                    const key = record[currentLevel];
                    if (!key) return acc;

                    if (!acc[key]) {
                        acc[key] = [];
                    }
                    acc[key].push(record);

                    return acc;
                }, {});

                // Convert to array and sort alphabetically by key
                return Object.entries(grouped)
                    .sort(([keyA], [keyB]) => keyA.localeCompare(keyB))
                    .map(([key, groupRecords]) => {
                        const node: { value: string; label: string; children?: Array<any> } = { value: key, label: key };

                        if (remainingLevels.length > 0) {
                            // Sort children recursively
                            node.children = buildTree(groupRecords, remainingLevels);
                        }

                        return node;
                    });
            };

            return buildTree(items, hierarchy);
        } catch (error) {
            console.error("Error building tree for cascader:", error);
            message.error("Failed to load options. Please try again later.");
            return [];
        }
    }, [items, hierarchy]);

    const filter = (inputValue: string, path: DefaultOptionType[]) =>
        path.some(
            (option) =>
                (option.label as string)
                    .toLowerCase()
                    .indexOf(inputValue.toLowerCase()) > -1
        );

    return (
        <Cascader
            className="cascading-search"
            style={{ width: "100%", borderRadius: 1 }}
            options={options}
            placeholder="Filter items..."
            displayRender={(label) => {
                try {
                    return (
                        <span
                            style={{ display: "flex", gap: 3, alignItems: "center" }}
                        >
                            {label.map((l, index) => (
                                <React.Fragment key={l}>
                                    {l}
                                    {index + 1 !== label.length && (
                                        <ChevronRightIcon sx={{ fontSize: 12 }} />
                                    )}
                                </React.Fragment>
                            ))}
                        </span>
                    );
                } catch (error) {
                    return label.join(" / ");
                }
            }}
            expandIcon={<ChevronRightIcon sx={{ fontSize: 12 }} />}
            changeOnSelect
            showSearch={{ filter, matchInputWidth: false }}
            onChange={onChange}
            value={value}
        />
    );
}
