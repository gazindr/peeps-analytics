mergeInto(LibraryManager.library, {
    HtmlFunnel_GetPlayerId: function () {
        var str = '';
        try {
            if (window.funnel && typeof window.funnel.getPlayerId === 'function')
                str = String(window.funnel.getPlayerId() || '');
        } catch (e) { /* ignore */ }
        var len = lengthBytesUTF8(str) + 1;
        var buffer = _malloc(len);
        stringToUTF8(str, buffer, len);
        return buffer;
    },

    HtmlFunnel_GetSessionId: function () {
        var str = '';
        try {
            if (window.funnel && typeof window.funnel.getSessionId === 'function')
                str = String(window.funnel.getSessionId() || '');
        } catch (e) { /* ignore */ }
        var len = lengthBytesUTF8(str) + 1;
        var buffer = _malloc(len);
        stringToUTF8(str, buffer, len);
        return buffer;
    },

    HtmlFunnel_GetElapsedSec: function () {
        try {
            if (window.funnel && typeof window.funnel.getElapsedSec === 'function')
                return window.funnel.getElapsedSec();
        } catch (e) { /* ignore */ }
        return 0;
    }
});
