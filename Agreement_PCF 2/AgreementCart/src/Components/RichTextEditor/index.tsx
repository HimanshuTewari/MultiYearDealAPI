import * as React from "react";
import { useEditor, EditorContent } from "@tiptap/react";
import type { Editor, Extensions } from "@tiptap/react";
import Typography from "@tiptap/extension-typography";
import Link from "@tiptap/extension-link";
import StarterKit from "@tiptap/starter-kit";
import "./index.css";

import FormatBoldIcon from "@mui/icons-material/FormatBold";
import FormatItalicIcon from "@mui/icons-material/FormatItalic";
import FormatStrikethroughIcon from "@mui/icons-material/FormatStrikethrough";
import FormatListNumberedIcon from "@mui/icons-material/FormatListNumbered";
import FormatListBulletedIcon from "@mui/icons-material/FormatListBulleted";
import LinkIcon from "@mui/icons-material/Link";
import LinkOffIcon from "@mui/icons-material/LinkOff";

export default function RichTextEditor({
    content = "",
    editable = true,
    onUpdate,
    disabled
}: {
    content?: string;
    editable?: boolean;
    onUpdate: (value: string) => void;
    disabled?: boolean;
}) {
    const extensions: Extensions = [
        StarterKit,
        Link.configure({
            linkOnPaste: false,
            openOnClick: true,
        }),
        Typography,
    ];

    function checkForContent(html: string) {
        try {
            const doc = new DOMParser().parseFromString(html, "text/html");
            return !!doc.body.textContent;
        } catch (error) {
            return false;
        }
    }

    const editor = useEditor({
        content,
        extensions,
        editable,
        onUpdate: ({ editor }) => {
            if (editable) {
                let html = editor.getHTML();

                const hasContent = checkForContent(html);
                if (!hasContent) html = "";

                onUpdate(html);
            }
        },
    }, [editable, content]);

    React.useEffect(() => {
        if (editor && content !== editor.getHTML()) {
            editor.commands.setContent(content);
        }
    }, [content, editor]);


    if (!editor) return null;
    return (
        <div className={`rich-text-editor ${editable && !!disabled ? "editable" : "not-editable"}`}>
            <div className="editor-input">
                <EditorContent editor={editor} />
            </div>
            <Toolbar editor={editor} editable={editable && !!disabled} />
        </div>
    );
}


function Toolbar({ editor, editable }: { editor: Editor; editable: boolean; }) {
    const isCursorOverLink = editor.getAttributes("link").href;

    return (
        <div className="rich-text-toolbar">
            <button
                className="icon"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().toggleBold().run() : () => null}
            >
                <FormatBoldIcon />
            </button>

            <button
                className="icon"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().toggleItalic().run() : () => null}
            >
                <FormatItalicIcon />
            </button>

            <button
                className="icon"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().toggleStrike().run() : () => null}
            >
                <FormatStrikethroughIcon />
            </button>

            <button
                className="icon heading"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().toggleHeading({ level: 1 }).run() : () => null}
            >
                H1
            </button>

            <button
                className="icon heading"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().toggleHeading({ level: 2 }).run() : () => null}
            >
                H2
            </button>

            <button
                className="icon heading"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().toggleHeading({ level: 3 }).run() : () => null}
            >
                H3
            </button>

            <button
                className="icon heading"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().toggleHeading({ level: 4 }).run() : () => null}
            >
                H4
            </button>

            <button
                className="icon heading"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().setParagraph().run() : () => null}
            >
                P
            </button>

            <button
                className="icon"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().toggleBulletList().run() : () => null}
            >
                <FormatListBulletedIcon />
            </button>

            <button
                className="icon"
                disabled={!editable}
                onClick={editable ? () => editor.chain().focus().toggleOrderedList().run() : () => null}
            >
                <FormatListNumberedIcon />
            </button>

            <div className="divider"></div>

            <button className="icon" disabled={!editable} onClick={editable ? () => setLink(editor) : () => null}>
                <LinkIcon />
            </button>

            <button
                disabled={!isCursorOverLink || !editable}
                className="icon"
                onClick={editable ? () => setLink(editor) : () => null}
            >
                <LinkOffIcon />
            </button>
        </div>
    );
}

function setLink(editor: Editor) {
    const previousUrl = editor.getAttributes('link').href
    const url = window.prompt('URL', previousUrl)

    // cancelled
    if (url === null) {
        return
    }

    // empty
    if (url === '') {
        editor.chain().focus().extendMarkRange('link').unsetLink().run()
        return
    }

    // update link
    editor.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
}