// 入力支援の共通スクリプト：
//  (1) Enterキーで「入力確定＋次項目へ移動」（Tab同等）。カナ項目はスキップ。
//  (2) 名称入力中のIME読み（ひらがな）を捕捉してカナ項目へ自動反映（簡易フリガナ）。
//      ※ローマ字IME入力時のみ有効。貼り付けや直接カナ入力・途中編集は近似動作。
(function () {
    "use strict";

    // ひらがな/カタカナ/長音/空白のみか
    const isKana = s => /^[぀-ヿーー　 ]*$/.test(s);
    // ひらがな→カタカナ
    const toKatakana = s => (s || "").replace(/[ぁ-ゖ]/g, c =>
        String.fromCharCode(c.charCodeAt(0) + 0x60));

    window.payInput = {
        _enterInit: false,

        // Enter で次の入力項目へ。document レベルなので動的に増えた項目にも効く。
        initEnterNav: function () {
            if (this._enterInit) return;
            this._enterInit = true;

            document.addEventListener("keydown", function (e) {
                if (e.key !== "Enter" || e.isComposing) return; // IME変換確定のEnterは無視
                const t = e.target;
                if (!(t instanceof HTMLInputElement)) return;

                const type = (t.type || "").toLowerCase();
                if (["checkbox", "radio", "button", "submit", "reset"].includes(type)) return;
                // MudSelect / オートコンプリートはコンポーネントの挙動に委ねる
                if (t.closest(".mud-select") || t.closest(".mud-autocomplete") ||
                    t.getAttribute("role") === "combobox") return;

                e.preventDefault();

                const fields = Array.from(
                    document.querySelectorAll("input:not([type=hidden]):not([disabled])"))
                    .filter(el => el.tabIndex !== -1 && el.offsetParent !== null && !el.closest(".mud-select"));

                const idx = fields.indexOf(t);
                if (idx < 0) return;
                for (let i = idx + 1; i < fields.length; i++) {
                    if (fields[i].closest(".js-enter-skip")) continue; // カナ項目はスキップ
                    fields[i].focus();
                    if (fields[i].select) fields[i].select();
                    return;
                }
            }, true);
        },

        // container 内の .js-name(名称) の入力から読みを推定し、dotnetRef.OnKana(カナ) を呼ぶ。
        bindAutoKana: function (container, dotnetRef) {
            if (!container) return;
            const nameEl = container.querySelector(".js-name input");
            if (!nameEl) return;

            let committed = "";  // 確定済みセグメントの読み
            let pending = "";    // 変換中セグメントの読み（漢字化前のひらがな）
            let composing = false;

            const push = () => dotnetRef.invokeMethodAsync("OnKana", toKatakana(committed + pending));

            nameEl.addEventListener("compositionstart", () => { composing = true; pending = ""; });
            nameEl.addEventListener("compositionupdate", e => {
                const d = e.data || "";
                if (isKana(d)) pending = d; // 漢字を含む更新は無視し、直前のひらがな読みを保持
            });
            nameEl.addEventListener("compositionend", () => {
                composing = false;
                committed += pending;
                pending = "";
                push();
            });
            nameEl.addEventListener("input", () => {
                if (composing) return;
                if (nameEl.value === "") { committed = ""; pending = ""; push(); } // 全消しでリセット
            });
        }
    };

    // 文字サイズ切替（小/中/大）：html の font-size を変え、rem ベースのフォント（MudBlazor 含む）を全体連動させる。
    // 設定は localStorage に保存し、起動時に復元する。勤怠管理DXPro のヘッダー機能を移植したもの。
    window.payUi = {
        _fontMap: { s: 15, m: 16, l: 18 },

        // size = "s" | "m" | "l"。html の font-size を px で設定し保存する。
        setFontScale: function (size) {
            const px = this._fontMap[size] || 16;
            document.documentElement.style.fontSize = px + "px";
            try { localStorage.setItem("fontScale", size); } catch (_) { }
            return size;
        },

        // 保存済みの文字サイズを返す（未設定は "m"）。
        getFontScale: function () {
            try { return localStorage.getItem("fontScale") || "m"; } catch (_) { return "m"; }
        },

        // 起動時：保存済みの文字サイズを適用し、その値を返す。
        initFontScale: function () {
            const size = this.getFontScale();
            this.setFontScale(size);
            return size;
        }
    };
})();
