# Fonte dos filtros Android

`easylist.json` e `LICENSE.txt` são cópias sem alteração do pacote
`src/LeanBrowser/Assets/uBOLite_2026.914.1325.edge.zip`, mantido por Raymond Hill e
colaboradores. O material EasyList é dos autores em https://easylist.to/.
O pacote completo continua sendo incorporado ao navegador Windows.

SHA-256 do pacote: `4a8d6c13f772dd53e0b1c2f7cbb8090d4df72987e723b946aeb8bbcc80921be5`.
SHA-256 de `easylist.json`: `0edfcb2a1bc1f46307355060072e8ce08d37fc174a1fc55136ddbc23224c71cb`.

Ao atualizar o uBlock Origin Lite, copie estes dois arquivos do novo pacote,
atualize a versão no nome desta pasta, em `build_filter_assets.js` e em
`src/CottonMobile/app/src/main/assets/FILTERS-NOTICE.txt`. Atualize também os hashes
acima. Na raiz do projeto, execute `node scripts/build-filter-assets.js`
para gerar os filtros e repita com `--check` para verificar sua reprodução.
