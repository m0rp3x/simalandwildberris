window.mappingStorage = {
    saveMapping: function (key, json) {
        localStorage.setItem(key, json);
    },
    loadMapping: function (key) {
        return localStorage.getItem(key);
    }
};
