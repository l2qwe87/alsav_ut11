const { execSync } = require('child_process')
const { existsSync, statSync } = require('fs')
const { join } = require('path')
const { z } = require("zod")

module.exports = {
  description: "Собрать расширение ext_b24 в CFU файл через конфигуратор 1С",
  args: {
    dbPath: z.string().default("F:\\src\\one_c\\alsav_ut").describe("Путь к файловой базе данных 1С"),
    outputPath: z.string().default("ext_b24.cfu").describe("Путь для сохранения CFU файла"),
  },
  async execute(args) {
    const { dbPath = "F:\\src\\one_c\\alsav_ut", outputPath = "ext_b24.cfu" } = args
    
    try {
      // Проверяем существование базы данных
      if (!existsSync(dbPath)) {
        return {
          success: false,
          message: "База данных 1С не найдена",
          error: `Путь ${dbPath} не существует`
        }
      }
      
      // Проверяем существование каталога расширения
      const extPath = join(process.cwd(), "ext_b24")
      if (!existsSync(extPath)) {
        return {
          success: false,
          message: "Каталог ext_b24 не найден",
          error: `Путь ${extPath} не существует`
        }
      }
      
      // Ищем исполняемый файл 1С
      const possiblePaths = [
        "C:\\Program Files\\1cv8\\common\\1cestart.exe",
        "C:\\Program Files\\1cv8\\8.3.22.1923\\bin\\1cv8.exe",
        "C:\\Program Files (x86)\\1cv8\\common\\1cestart.exe",
        "C:\\Program Files (x86)\\1cv8\\8.3.22.1923\\bin\\1cv8.exe"
      ]
      
      let designerPath = null
      for (const path of possiblePaths) {
        if (existsSync(path)) {
          designerPath = path
          break
        }
      }
      
      if (!designerPath) {
        return {
          success: false,
          message: "Конфигуратор 1С не найден",
          error: "Не найден исполняемый файл 1С в стандартных путях"
        }
      }
      
      console.log(`Используем конфигуратор: ${designerPath}`)
      console.log(`База данных: ${dbPath}`)
      console.log(`Расширение: ${extPath}`)
      
      // Команда 1: Загрузка конфигурации из файлов с флагом -Extension
      const loadCommand = `"${designerPath}" DESIGNER /F"${dbPath}" /LoadConfigFromFiles "${extPath}" -Extension`
      
      console.log("Выполняем команду загрузки:")
      console.log(loadCommand)
      
      try {
        execSync(loadCommand, { stdio: 'inherit', timeout: 300000 })
        console.log("Загрузка конфигурации завершена")
      } catch (error) {
        return {
          success: false,
          message: "Ошибка при загрузке конфигурации",
          error: error.message
        }
      }
      
      // Команда 2: Выгрузка конфигурации базы данных в CFU
      const dumpCommand = `"${designerPath}" DESIGNER /F"${dbPath}" /DumpDBCfg "${outputPath}"`
      
      console.log("Выполняем команду выгрузки:")
      console.log(dumpCommand)
      
      try {
        execSync(dumpCommand, { stdio: 'inherit', timeout: 300000 })
        console.log("Выгрузка конфигурации завершена")
      } catch (error) {
        return {
          success: false,
          message: "Ошибка при выгрузке конфигурации",
          error: error.message
        }
      }
      
      // Проверяем результат
      if (existsSync(outputPath)) {
        const stats = statSync(outputPath)
        return {
          success: true,
          message: "CFU файл успешно создан через конфигуратор 1С",
          outputPath: outputPath,
          size: stats.size
        }
      } else {
        return {
          success: false,
          message: "CFU файл не был создан",
          error: "Неизвестная ошибка"
        }
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