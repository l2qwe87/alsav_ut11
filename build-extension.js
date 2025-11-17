const buildCfu = require('./.opencode/tool/build-cfu.js');

buildCfu.execute({
  outputPath: 'ext_alsav.cfu',
  includeExtensions: false
}).then(result => {
  console.log('CFU файл успешно создан:');
  console.log('Путь:', result.outputPath);
  console.log('Размер:', result.size, 'байт');
  console.log('Объектов метаданных:', result.metadataItems);
}).catch(error => {
  console.error('Ошибка:', error.message);
});