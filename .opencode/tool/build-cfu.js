const { readFileSync, writeFileSync, readdirSync, statSync } = require('fs')
const { join } = require('path')

module.exports = {
  description: "Собрать каталог ext_alsav в CFU файл",
  args: {
    outputPath: {
      type: "string",
      description: "Путь для сохранения CFU файла (по умолчанию: ext_alsav.cfu)",
      default: "ext_alsav.cfu"
    },
    includeExtensions: {
      type: "boolean", 
      description: "Включать расширения .xml в имена файлов",
      default: false
    }
  },
  async execute(args) {
    const { outputPath = "ext_alsav.cfu", includeExtensions = false } = args
    
    try {
      const extAlsavPath = join(process.cwd(), "ext_alsav")
      
      // Проверяем существование каталога
      if (!statSync(extAlsavPath).isDirectory()) {
        return {
          success: false,
          message: "Каталог ext_alsav не найден",
          error: `Путь ${extAlsavPath} не существует`
        }
      }
      
      let cfuContent = `<?xml version="1.0" encoding="UTF-8"?>
<ConfiguratorInfo xmlns="http://v8.1c.ru/configurator">
  <Metadata format="Configurator">
`
      
      // Рекурсивно обходим каталог
      const processDirectory = (dirPath, relativePath = "") => {
        const items = readdirSync(dirPath)
        
        for (const item of items) {
          const itemPath = join(dirPath, item)
          const itemRelativePath = relativePath ? join(relativePath, item) : item
          const stat = statSync(itemPath)
          
          if (stat.isDirectory()) {
            // Пропускаем служебные каталоги
            if (item.startsWith(".")) {
              continue
            }
            
            cfuContent += `    <Metadata item="${item}" type="${getMetadataType(itemRelativePath)}">\n`
            processDirectory(itemPath, itemRelativePath)
            cfuContent += `    </Metadata>\n`
          } else if (item.endsWith(".xml")) {
            // Обрабатываем XML файлы
            let fileName = item
            if (!includeExtensions) {
              fileName = item.replace(".xml", "")
            }
            
            cfuContent += `    <Metadata item="${fileName}" type="${getMetadataType(itemRelativePath)}"/>\n`
          }
        }
      }
      
      // Определяем тип метаданных по пути
      const getMetadataType = (filePath) => {
        if (filePath.includes("AccumulationRegisters")) return "AccumulationRegister"
        if (filePath.includes("InformationRegisters")) return "InformationRegister"
        if (filePath.includes("Catalogs")) return "Catalog"
        if (filePath.includes("Documents")) return "Document"
        if (filePath.includes("CommonModules")) return "CommonModule"
        if (filePath.includes("DataProcessors")) return "DataProcessor"
        if (filePath.includes("Reports")) return "Report"
        if (filePath.includes("Enums")) return "Enum"
        if (filePath.includes("ChartsOfCharacteristicTypes")) return "ChartOfCharacteristicTypes"
        if (filePath.includes("BusinessProcesses")) return "BusinessProcess"
        if (filePath.includes("Tasks")) return "Task"
        if (filePath.includes("Roles")) return "Role"
        if (filePath.includes("Subsystems")) return "Subsystem"
        if (filePath.includes("CommonForms")) return "CommonForm"
        if (filePath.includes("CommonPictures")) return "CommonPicture"
        if (filePath.includes("Constants")) return "Constant"
        if (filePath.includes("FunctionalOptions")) return "FunctionalOption"
        if (filePath.includes("DefinedTypes")) return "DefinedType"
        if (filePath.includes("CommandGroups")) return "CommandGroup"
        if (filePath.includes("CommonCommands")) return "CommonCommand"
        if (filePath.includes("StyleItems")) return "StyleItem"
        if (filePath.includes("Languages")) return "Language"
        return "Catalog" // по умолчанию
      }
      
      // Начинаем обработку с корневых элементов
      processDirectory(extAlsavPath)
      
      cfuContent += `  </Metadata>
</ConfiguratorInfo>`
      
      // Сохраняем CFU файл
      writeFileSync(outputPath, cfuContent, 'utf8')
      
      const stats = statSync(outputPath)
      
      return {
        success: true,
        message: "CFU файл успешно создан",
        outputPath: outputPath,
        size: stats.size,
        metadataItems: countMetadataItems(extAlsavPath)
      }
      
    } catch (error) {
      return {
        success: false,
        message: "Ошибка при создании CFU файла",
        error: error.message
      }
    }
  }
}

// Вспомогательная функция для подсчета элементов метаданных
function countMetadataItems(dirPath) {
  let count = 0
  
  const countInDirectory = (currentPath) => {
    try {
      const items = readdirSync(currentPath)
      
      for (const item of items) {
        const itemPath = join(currentPath, item)
        const stat = statSync(itemPath)
        
        if (stat.isDirectory() && !item.startsWith(".")) {
          countInDirectory(itemPath)
        } else if (item.endsWith(".xml")) {
          count++
        }
      }
    } catch (error) {
      // Пропускаем ошибки доступа
    }
  }
  
  countInDirectory(dirPath)
  return count
}