import { execSync } from "child_process"
import { z } from "zod"

export default {
  description: "Проверить синтаксис 1С файла с помощью платформы 1С",
  args: {
    filePath: z.string().describe("Путь к файлу 1С для проверки синтаксиса"),
  },
  async execute(args) {
    const { filePath } = args;
    const platformPath = "Z:\\Apps\\1cv8\\8.3.27.1859\\bin\\1cv8.exe";
    
    try {
      // Проверяем существование файла
      const fs = await import('fs');
      if (!fs.existsSync(filePath)) {
        return {
          success: false,
          message: "Файл не найден",
          error: `Файл ${filePath} не существует`
        };
      }
      
      // Команда для проверки синтаксиса 1С
      const result = execSync(
        `"${platformPath}" /CONFIG /F"${filePath}" /CheckSyntax`,
        { encoding: 'utf8', timeout: 30000, stdio: ['ignore', 'pipe', 'pipe'] }
      );
      
      return {
        success: true,
        message: "Синтаксис проверен успешно",
        output: result
      };
    } catch (error) {
      return {
        success: false,
        message: "Ошибка синтаксиса",
        error: error.message,
        stderr: error.stderr ? error.stderr.toString() : ""
      };
    }
  }
}