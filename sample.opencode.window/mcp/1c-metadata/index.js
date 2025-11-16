#!/usr/bin/env node

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import { readFileSync, readdirSync, statSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

class Metadata1CServer {
  constructor() {
    this.server = new Server(
      {
        name: "1c-metadata-mcp",
        version: "1.0.0",
      },
      {
        capabilities: {
          tools: {},
        },
      }
    );

    this.setupToolHandlers();
  }

  setupToolHandlers() {
    this.server.setRequestHandler(ListToolsRequestSchema, async () => {
      return {
        tools: [
          {
            name: "search_metadata",
            description: "Поиск по метаданным конфигурации 1С",
            inputSchema: {
              type: "object",
              properties: {
                query: {
                  type: "string",
                  description: "Текст для поиска в метаданных"
                },
                metadataType: {
                  type: "string",
                  description: "Тип метаданных (Catalogs, Documents, Registers, etc.)",
                  enum: ["Catalogs", "Documents", "AccumulationRegisters", "InformationRegisters", 
                         "CommonModules", "DataProcessors", "Reports", "Enums", "ChartsOfCharacteristicTypes"]
                }
              }
            }
          },
          {
            name: "get_metadata_structure",
            description: "Получить структуру метаданных конфигурации 1С",
            inputSchema: {
              type: "object",
              properties: {
                path: {
                  type: "string",
                  description: "Путь к директории с метаданными (по умолчанию текущая)"
                }
              }
            }
          },
          {
            name: "find_object_by_name",
            description: "Найти объект метаданных по имени",
            inputSchema: {
              type: "object",
              properties: {
                objectName: {
                  type: "string",
                  description: "Имя объекта для поиска"
                }
              }
            }
          }
        ]
      };
    });

    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      const { name, arguments: args } = request.params;

      try {
        switch (name) {
          case "search_metadata":
            return await this.searchMetadata(args.query, args.metadataType);
          
          case "get_metadata_structure":
            return await this.getMetadataStructure(args.path);
          
          case "find_object_by_name":
            return await this.findObjectByName(args.objectName);
          
          default:
            throw new Error(`Unknown tool: ${name}`);
        }
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Error: ${error.message}`
            }
          ]
        };
      }
    });
  }

  async searchMetadata(query, metadataType) {
    const projectRoot = process.cwd();
    const results = [];
    
    // Поиск в XML файлах метаданных
    const searchInDirectory = (dir, type = null) => {
      try {
        const items = readdirSync(dir);
        
        for (const item of items) {
          const fullPath = join(dir, item);
          const stat = statSync(fullPath);
          
          if (stat.isDirectory()) {
            searchInDirectory(fullPath, type);
          } else if (item.endsWith('.xml')) {
            try {
              const content = readFileSync(fullPath, 'utf8');
              
              // Проверяем тип метаданных если указан
              if (type && !fullPath.includes(type)) {
                continue;
              }
              
              // Поиск по содержимому XML
              if (query && content.toLowerCase().includes(query.toLowerCase())) {
                const relativePath = fullPath.replace(projectRoot, '');
                results.push({
                  path: relativePath,
                  type: this.detectMetadataType(fullPath),
                  matches: this.extractMatches(content, query)
                });
              }
            } catch (error) {
              // Пропускаем файлы которые не удалось прочитать
            }
          }
        }
      } catch (error) {
        // Пропускаем директории которые не удалось прочитать
      }
    };

    // Определяем директории для поиска
    const searchDirs = ['ext_alsav', 'ext_b24'];
    
    for (const searchDir of searchDirs) {
      const fullPath = join(projectRoot, searchDir);
      if (statSync(fullPath).isDirectory()) {
        searchInDirectory(fullPath, metadataType);
      }
    }

    return {
      content: [
        {
          type: "text",
          text: JSON.stringify({
            query,
            metadataType,
            found: results.length,
            results
          }, null, 2)
        }
      ]
    };
  }

  async getMetadataStructure(path) {
    const projectRoot = path || process.cwd();
    const structure = {};
    
    const scanDirectory = (dir, basePath = '') => {
      try {
        const items = readdirSync(dir);
        
        for (const item of items) {
          const fullPath = join(dir, item);
          const stat = statSync(fullPath);
          
          if (stat.isDirectory()) {
            const relativePath = join(basePath, item);
            
            // Определяем тип метаданных по имени директории
            if (this.isMetadataType(item)) {
              structure[item] = {
                type: item,
                objects: []
              };
              
              // Сканируем объекты внутри типа метаданных
              this.scanMetadataObjects(fullPath, structure[item]);
            } else {
              scanDirectory(fullPath, relativePath);
            }
          }
        }
      } catch (error) {
        // Пропускаем директории которые не удалось прочитать
      }
    };

    scanDirectory(projectRoot);

    return {
      content: [
        {
          type: "text",
          text: JSON.stringify(structure, null, 2)
        }
      ]
    };
  }

  async findObjectByName(objectName) {
    const projectRoot = process.cwd();
    const results = [];
    
    const searchInDirectory = (dir) => {
      try {
        const items = readdirSync(dir);
        
        for (const item of items) {
          const fullPath = join(dir, item);
          const stat = statSync(fullPath);
          
          if (stat.isDirectory()) {
            // Проверяем имя директории
            if (item.toLowerCase().includes(objectName.toLowerCase())) {
              results.push({
                path: fullPath.replace(projectRoot, ''),
                type: 'directory',
                metadataType: this.detectMetadataType(fullPath)
              });
            }
            searchInDirectory(fullPath);
          } else if (item.endsWith('.xml')) {
            // Проверяем имя файла
            if (item.toLowerCase().includes(objectName.toLowerCase())) {
              results.push({
                path: fullPath.replace(projectRoot, ''),
                type: 'file',
                metadataType: this.detectMetadataType(fullPath)
              });
            }
          }
        }
      } catch (error) {
        // Пропускаем директории которые не удалось прочитать
      }
    };

    const searchDirs = ['ext_alsav', 'ext_b24'];
    
    for (const searchDir of searchDirs) {
      const fullPath = join(projectRoot, searchDir);
      if (statSync(fullPath).isDirectory()) {
        searchInDirectory(fullPath);
      }
    }

    return {
      content: [
        {
          type: "text",
          text: JSON.stringify({
            objectName,
            found: results.length,
            results
          }, null, 2)
        }
      ]
    };
  }

  detectMetadataType(filePath) {
    const types = [
      'Catalogs', 'Documents', 'AccumulationRegisters', 'InformationRegisters',
      'CommonModules', 'DataProcessors', 'Reports', 'Enums', 'ChartsOfCharacteristicTypes',
      'BusinessProcesses', 'Tasks', 'Roles', 'Subsystems', 'CommonForms', 'CommonPictures'
    ];
    
    for (const type of types) {
      if (filePath.includes(type)) {
        return type;
      }
    }
    
    return 'Unknown';
  }

  isMetadataType(dirName) {
    const types = [
      'Catalogs', 'Documents', 'AccumulationRegisters', 'InformationRegisters',
      'CommonModules', 'DataProcessors', 'Reports', 'Enums', 'ChartsOfCharacteristicTypes',
      'BusinessProcesses', 'Tasks', 'Roles', 'Subsystems', 'CommonForms', 'CommonPictures',
      'Constants', 'FunctionalOptions', 'DefinedTypes', 'CommandGroups', 'CommonCommands',
      'StyleItems', 'Languages'
    ];
    
    return types.includes(dirName);
  }

  scanMetadataObjects(dirPath, structureItem) {
    try {
      const items = readdirSync(dirPath);
      
      for (const item of items) {
        const fullPath = join(dirPath, item);
        const stat = statSync(fullPath);
        
        if (stat.isDirectory() && !item.startsWith('.')) {
          structureItem.objects.push({
            name: item,
            path: fullPath.replace(process.cwd(), ''),
            hasXml: statSync(join(fullPath, item + '.xml')).isFile()
          });
        }
      }
    } catch (error) {
      // Пропускаем ошибки
    }
  }

  extractMatches(content, query) {
    const lines = content.split('\n');
    const matches = [];
    
    lines.forEach((line, index) => {
      if (line.toLowerCase().includes(query.toLowerCase())) {
        matches.push({
          line: index + 1,
          content: line.trim()
        });
      }
    });
    
    return matches.slice(0, 10); // Ограничиваем количество совпадений
  }

  async run() {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    console.error("1C Metadata MCP server running on stdio");
  }
}

const server = new Metadata1CServer();
server.run().catch(console.error);